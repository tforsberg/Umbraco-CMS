using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Newtonsoft.Json;
using Umbraco.Core;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Sync;
using umbraco.interfaces;
using Umbraco.Web.Routing;

namespace Umbraco.Web
{
    internal class BatchedDatabaseServerMessenger : DatabaseServerMessenger
    {
        internal BatchedDatabaseServerMessenger(bool enableDistCalls) : base(enableDistCalls)
        {
            UmbracoModule.EndRequest += UmbracoModule_EndRequest;
            UmbracoModule.RouteAttempt += UmbracoModule_RouteAttempt;
        }

        void UmbracoModule_RouteAttempt(object sender, Routing.RoutableAttemptEventArgs e)
        {
            switch (e.Outcome)
            {
                case EnsureRoutableOutcome.IsRoutable:
                    Sync();
                    break;
                case EnsureRoutableOutcome.NotDocumentRequest:
                    //so it's not a document request, we'll check if it's a back office request
                    if (e.HttpContext.Request.Url.IsBackOfficeRequest(HttpRuntime.AppDomainAppVirtualPath))
                    {
                        //it's a back office request, we should sync!
                        Sync();
                    }
                    break;
                case EnsureRoutableOutcome.NotReady:
                case EnsureRoutableOutcome.NotConfigured:
                case EnsureRoutableOutcome.NoContent:
                default:
                    break;
            }

        }

        void UmbracoModule_EndRequest(object sender, EventArgs e)
        {
            if (HttpContext.Current == null)
            {
                return;
            }

            var items = HttpContext.Current.Items[typeof(BatchedServerMessenger).Name] as List<DistributedMessage>;
            if (items != null)
            {
                var copied = new DistributedMessage[items.Count];
                items.CopyTo(copied);
                //now set to null so it get's cleaned up on this request
                HttpContext.Current.Items[typeof(BatchedServerMessenger).Name] = null;

                SubmitInstructions(copied);
            }
        }

        protected override void PerformDistributedCall(IEnumerable<IServerAddress> servers, ICacheRefresher refresher, MessageType dispatchType, IEnumerable<object> ids = null, Type idArrayType = null, string jsonPayload = null)
        {
            //NOTE: we use UmbracoContext instead of HttpContext.Current because when some web methods run async, the 
            // HttpContext.Current is null but the UmbracoContext.Current won't be since we manually assign it.
            if (UmbracoContext.Current == null || UmbracoContext.Current.HttpContext == null)
            {
                throw new NotSupportedException("This messenger cannot execute without a valid/current UmbracoContext with an HttpContext assigned");
            }

            if (UmbracoContext.Current.HttpContext.Items[typeof(BatchedServerMessenger).Name] == null)
            {
                UmbracoContext.Current.HttpContext.Items[typeof(BatchedServerMessenger).Name] = new List<DistributedMessage>();
            }
            var list = (List<DistributedMessage>)UmbracoContext.Current.HttpContext.Items[typeof(BatchedServerMessenger).Name];

            list.Add(new DistributedMessage
            {
                DispatchType = dispatchType,
                IdArrayType = idArrayType,
                Ids = ids,
                JsonPayload = jsonPayload,
                Refresher = refresher,
                Servers = servers
            });
        }

        private void SubmitInstructions(IEnumerable<DistributedMessage> messages)
        {
            var instructions = messages.Select(DistributedMessage.ConvertToInstructions);
            var dto = new CacheInstructionDto
            {
                UtcStamp = DateTime.UtcNow,
                JsonInstruction = JsonConvert.SerializeObject(instructions, Formatting.None)
            };
            ApplicationContext.Current.DatabaseContext.Database.Insert(dto);
        }
    }
}