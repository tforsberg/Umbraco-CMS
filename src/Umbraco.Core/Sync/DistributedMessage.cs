using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using umbraco.interfaces;

namespace Umbraco.Core.Sync
{
    internal class DistributedMessage
    {
        public IEnumerable<IServerAddress> Servers { get; set; }
        public ICacheRefresher Refresher { get; set; }
        public MessageType DispatchType { get; set; }
        public IEnumerable<object> Ids { get; set; }
        public Type IdArrayType { get; set; }
        public string JsonPayload { get; set; }

        internal static RefreshInstruction[] ConvertToInstructions(DistributedMessage msg)
        {
            switch (msg.DispatchType)
            {
                case MessageType.RefreshAll:
                    return new[]
                    {
                        new RefreshInstruction
                        {
                            RefreshType = RefreshMethodType.RefreshAll,
                            RefresherId = msg.Refresher.UniqueIdentifier
                        }
                    };
                case MessageType.RefreshById:
                    if (msg.IdArrayType == null)
                    {
                        throw new InvalidOperationException("Cannot refresh by id if the idArrayType is null");
                    }

                    if (msg.IdArrayType == typeof(int))
                    {
                        var serializer = new JavaScriptSerializer();
                        var jsonIds = serializer.Serialize(msg.Ids.Cast<int>().ToArray());

                        return new[]
                        {
                            new RefreshInstruction
                            {
                                JsonIds = jsonIds,
                                RefreshType = RefreshMethodType.RefreshByIds,
                                RefresherId = msg.Refresher.UniqueIdentifier
                            }
                        };
                    }

                    return msg.Ids.Select(x => new RefreshInstruction
                    {
                        GuidId = (Guid)x,
                        RefreshType = RefreshMethodType.RefreshById,
                        RefresherId = msg.Refresher.UniqueIdentifier
                    }).ToArray();

                case MessageType.RefreshByJson:
                    return new[]
                    {
                        new RefreshInstruction
                        {
                            RefreshType = RefreshMethodType.RefreshByJson,
                            RefresherId = msg.Refresher.UniqueIdentifier,
                            JsonPayload = msg.JsonPayload
                        }
                    };
                case MessageType.RemoveById:
                    return msg.Ids.Select(x => new RefreshInstruction
                    {
                        IntId = (int)x,
                        RefreshType = RefreshMethodType.RemoveById,
                        RefresherId = msg.Refresher.UniqueIdentifier
                    }).ToArray();
                case MessageType.RefreshByInstance:
                case MessageType.RemoveByInstance:
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}