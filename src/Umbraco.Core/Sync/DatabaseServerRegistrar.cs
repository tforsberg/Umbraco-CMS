using System;
using System.Collections.Generic;
using Umbraco.Core.Services;

namespace Umbraco.Core.Sync
{
    /// <summary>
    /// A registrar that stores registered server nodes in a database
    /// </summary>
    public sealed class DatabaseServerRegistrar : IServerRegistrar
    {
        public DatabaseServerRegistrarOptions Options { get; private set; }

        

        private readonly Lazy<ServerRegistrationService> _registrationService;

        public DatabaseServerRegistrar(Lazy<ServerRegistrationService> registrationService, DatabaseServerRegistrarOptions options)
        {
            Options = options;
            if (registrationService == null) throw new ArgumentNullException("registrationService");
            if (options == null) throw new ArgumentNullException("options");
            _registrationService = registrationService;
        }

        public IEnumerable<IServerAddress> Registrations
        {
            get { return _registrationService.Value.GetActiveServers(); }
        }
    }
}