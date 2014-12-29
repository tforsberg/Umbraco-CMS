using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core.Cache;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.Mappers;
using umbraco.interfaces;

namespace Umbraco.Core.Sync
{
    public class DatabaseServerMessenger : DefaultServerMessenger
    {
        private readonly ApplicationContext _appContext;
        private readonly DatabaseServerMessengerOptions _options;
        private readonly object _lock = new object();
        private int _lastId = -1;
        private volatile bool _syncing = false;
        private long _lastUtcTicks;
        private bool _initialized = false;

        public DatabaseServerMessenger(ApplicationContext appContext, bool enableDistCalls, DatabaseServerMessengerOptions options)
            : base(() => enableDistCalls
                //This is simply to ensure that dist calls gets enabled on the base messenger - a bit of a hack but works
                ? new Tuple<string, string>("empty", "empty")
                : null)
        {
            if (appContext == null) throw new ArgumentNullException("appContext");
            if (options == null) throw new ArgumentNullException("options");
            _appContext = appContext;
            _options = options;
            _lastUtcTicks = DateTime.UtcNow.Ticks;
            UmbracoApplicationBase.ApplicationStarted += OnApplicationStarted;
        }

        /// <summary>
        /// A check to see if a distributed call should be made or only to refresh on the single instance
        /// </summary>
        /// <param name="servers"></param>
        /// <param name="refresher"></param>
        /// <param name="dispatchType"></param>
        /// <returns></returns>
        protected override bool ShouldMakeDistributedCall(IEnumerable<IServerAddress> servers, ICacheRefresher refresher, MessageType dispatchType)
        {
            
            if (_initialized == false)
            {
                return false;
            }

            //we don't care if there's servers listed or not, if distributed call is enabled we will make the call
            return UseDistributedCalls;
        }

        protected override void PerformDistributedCall(
            IEnumerable<IServerAddress> servers, 
            ICacheRefresher refresher, 
            MessageType dispatchType, 
            IEnumerable<object> ids = null, 
            Type idArrayType = null, 
            string jsonPayload = null)
        {
            var msg = new DistributedMessage
            {
                DispatchType = dispatchType,
                IdArrayType = idArrayType,
                Ids = ids,
                JsonPayload = jsonPayload,
                Refresher = refresher,
                Servers = servers
            };

            var instructions = DistributedMessage.ConvertToInstructions(msg);
            var dto = new CacheInstructionDto
            {
                UtcStamp = DateTime.UtcNow,
                JsonInstruction = JsonConvert.SerializeObject(instructions, Formatting.None)
            };
            _appContext.DatabaseContext.Database.Insert(dto);
        }

        /// <summary>
        /// Save the latest id in the db as the last synced
        /// </summary>
        /// <remarks>
        /// THIS IS NOT THREAD SAFE
        /// </remarks>
        internal void FirstSync()
        {
            //we haven't synced - in this case we aren't going to sync the whole thing, we will assume this is a new 
            // server and it will need to rebuild it's own persisted cache. Currently in that case it is Lucene and the xml
            // cache file.
            LogHelper.Warn<DatabaseServerMessenger>("No last synced Id found, this generally means this is a new server/install. The server will rebuild its caches and indexes and then adjust it's last synced id to the latest found in the database and will start maintaining cache updates based on that id");
            
            //perform rebuilds if specified
            if (_options.RebuildingCallbacks != null)
            {
                foreach (var callback in _options.RebuildingCallbacks)
                {
                    callback();
                }
            }
            
            
            //go get the last id in the db and store it
            var lastId = _appContext.DatabaseContext.Database.ExecuteScalar<int>(
                "SELECT MAX(id) FROM umbracoCacheInstruction");
            if (lastId > 0)
            {
                SaveLastSynced(lastId);
            }
        }

        internal void Sync()
        {

            //don't process, this is not in the timeframe - we don't want to check the db every request, only once if it's been at least 5 seconds.
            if (TimeSpan.FromTicks(DateTime.UtcNow.Ticks).TotalSeconds - TimeSpan.FromTicks(_lastUtcTicks).TotalSeconds <= _options.ThrottleSeconds)
            {
                //NOTE: Removed logging as it will just keep showing this and people will wonder why.
                //LogHelper.Debug<DatabaseServerMessenger>("Skipping distributed sync, not in timeframe");
                return;
            }

            if (_syncing == false)
            {
                lock (_lock)
                {
                    if (_syncing == false)
                    {
                        //set the flag so other threads don't attempt
                        _syncing = true;
                        _lastUtcTicks = DateTime.UtcNow.Ticks;

                        using (DisposableTimer.DebugDuration<DatabaseServerMessenger>("Syncing from database..."))
                        {
                            //get the outstanding items

                            var sql = new Sql().Select("*")
                                .From<CacheInstructionDto>()
                                .Where<CacheInstructionDto>(dto => dto.Id > _lastId)
                                .OrderBy<CacheInstructionDto>(dto => dto.Id);

                            var list = _appContext.DatabaseContext.Database.Fetch<CacheInstructionDto>(sql);

                            if (list.Count > 0)
                            {
                                foreach (var item in list)
                                {
                                    try
                                    {
                                        var jsonArray = JsonConvert.DeserializeObject<JArray>(item.JsonInstruction);
                                        UpdateRefreshers(jsonArray);
                                    }
                                    catch (JsonException ex)
                                    {
                                        LogHelper.Error<DatabaseServerMessenger>("Could not deserialize a distributed cache instruction! Value: " + item.JsonInstruction, ex);
                                    }
                                }

                                SaveLastSynced(list.Max(x => x.Id));
                            }

                            //prune old records
                            _appContext.DatabaseContext.Database.Delete<CacheInstructionDto>("WHERE utcStamp < @pruneDate", new {pruneDate = DateTime.UtcNow.AddDays(_options.DaysToRetainInstructionRecords*-1)});

                            //reset
                            _syncing = false;
                        }
                        
                    }
                }
            }
        }

        internal void UpdateRefreshers(JArray jsonArray)
        {
            foreach (var jsonItem in jsonArray)
            {
                //This could be a JObject in which case we can convert to a RefreshInstruction, otherwise it could be 
                // another JArray in which case we'll iterate that.
                var jsonObj = jsonItem as JObject;
                if (jsonObj != null)
                {
                    var instruction = jsonObj.ToObject<RefreshInstruction>();

                    //now that we have the instruction, just process it

                    switch (instruction.RefreshType)
                    {
                        case RefreshMethodType.RefreshAll:
                            RefreshAll(instruction.RefresherId);
                            break;
                        case RefreshMethodType.RefreshByGuid:
                            RefreshByGuid(instruction.RefresherId, instruction.GuidId);
                            break;
                        case RefreshMethodType.RefreshById:
                            RefreshById(instruction.RefresherId, instruction.IntId);
                            break;
                        case RefreshMethodType.RefreshByIds:
                            RefreshByIds(instruction.RefresherId, instruction.JsonIds);
                            break;
                        case RefreshMethodType.RefreshByJson:
                            RefreshByJson(instruction.RefresherId, instruction.JsonPayload);
                            break;
                        case RefreshMethodType.RemoveById:
                            RemoveById(instruction.RefresherId, instruction.IntId);
                            break;
                    }

                }
                else
                {
                    var jsonInnerArray = (JArray)jsonItem;
                    //recurse
                    UpdateRefreshers(jsonInnerArray);
                }
            }
        }

        internal void ReadLastSynced()
        {
            var tempFolder = IOHelper.MapPath("~/App_Data/TEMP/DistCache");
            var file = Path.Combine(tempFolder, NetworkHelper.FileSafeMachineName + "-lastsynced.txt");
            if (File.Exists(file))
            {
                var content = File.ReadAllText(file);
                int last;
                if (int.TryParse(content, out last))
                {
                    _lastId = last;
                }
            }
        }

        /// <summary>
        /// Set the in-memory last-synced id and write to file
        /// </summary>
        /// <param name="id"></param>
        /// <remarks>
        /// THIS IS NOT THREAD SAFE
        /// </remarks>
        private void SaveLastSynced(int id)
        {
            _lastId = id;
            var tempFolder = IOHelper.MapPath("~/App_Data/TEMP/DistCache");
            if (Directory.Exists(tempFolder) == false)
            {
                Directory.CreateDirectory(tempFolder);
            }
            //save the file
            File.WriteAllText(Path.Combine(tempFolder, NetworkHelper.FileSafeMachineName + "-lastsynced.txt"), id.ToString(CultureInfo.InvariantCulture));
        }

     
        #region Updates the refreshers
        private void RefreshAll(Guid uniqueIdentifier)
        {
            var cr = CacheRefreshersResolver.Current.GetById(uniqueIdentifier);
            cr.RefreshAll();
        }

        private void RefreshByGuid(Guid uniqueIdentifier, Guid Id)
        {
            var cr = CacheRefreshersResolver.Current.GetById(uniqueIdentifier);
            cr.Refresh(Id);
        }

        private void RefreshById(Guid uniqueIdentifier, int Id)
        {
            var cr = CacheRefreshersResolver.Current.GetById(uniqueIdentifier);
            cr.Refresh(Id);
        }

        private void RefreshByIds(Guid uniqueIdentifier, string jsonIds)
        {
            var serializer = new JavaScriptSerializer();
            var ids = serializer.Deserialize<int[]>(jsonIds);

            var cr = CacheRefreshersResolver.Current.GetById(uniqueIdentifier);
            foreach (var i in ids)
            {
                cr.Refresh(i);
            }
        }

        private void RefreshByJson(Guid uniqueIdentifier, string jsonPayload)
        {
            var cr = CacheRefreshersResolver.Current.GetById(uniqueIdentifier) as IJsonCacheRefresher;
            if (cr == null)
            {
                throw new InvalidOperationException("The cache refresher: " + uniqueIdentifier + " is not of type " + typeof(IJsonCacheRefresher));
            }
            cr.Refresh(jsonPayload);
        }

        private void RemoveById(Guid uniqueIdentifier, int Id)
        {
            var cr = CacheRefreshersResolver.Current.GetById(uniqueIdentifier);
            cr.Remove(Id);
        } 
        #endregion

        /// <summary>
        /// Ensure we can connect to the db and initialize
        /// </summary>
        public void OnApplicationStarted(object sender, EventArgs eventArgs)
        {
            if (_appContext.IsConfigured && _appContext.DatabaseContext.IsDatabaseConfigured && _appContext.DatabaseContext.CanConnect)
            {
                ReadLastSynced();

                //if there's been nothing sync, perform a first sync, this will store the latest id
                if (_lastId == -1)
                {
                    FirstSync();
                }

                _initialized = true;
            }
            else
            {
                LogHelper.Warn<DatabaseServerMessenger>("The app is not configured or cannot connect to the database, this server cannot be initialized with " + typeof(DatabaseServerMessenger) + ", distributed calls will not be enabled for this server");
            }
            
        }
    }
}