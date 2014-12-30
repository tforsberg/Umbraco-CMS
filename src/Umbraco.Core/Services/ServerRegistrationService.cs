using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Services
{

    /// <summary>
    /// Service to manage server registrations in the database
    /// </summary>
    public sealed class ServerRegistrationService
    {
        private readonly RepositoryFactory _repositoryFactory;
        private readonly IDatabaseUnitOfWorkProvider _uowProvider;

        public ServerRegistrationService()
            : this(new RepositoryFactory())
        { }

        public ServerRegistrationService(RepositoryFactory repositoryFactory)
            : this(new PetaPocoUnitOfWorkProvider(), repositoryFactory)
        { }

        public ServerRegistrationService(IDatabaseUnitOfWorkProvider provider, RepositoryFactory repositoryFactory)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (repositoryFactory == null) throw new ArgumentNullException("repositoryFactory");
            _uowProvider = provider;
            _repositoryFactory = repositoryFactory;
        }

        /// <summary>
        /// Called to 'call home' to ensure the current server has an active record - this also flags stale servers as inactive
        /// </summary>
        /// <param name="address"></param>
        /// <param name="computerName"></param>
        /// <param name="staleServerTimeout"></param>
        public void EnsureActive(string address, string computerName, TimeSpan staleServerTimeout)
        {
            var uow = _uowProvider.GetUnitOfWork();
            using (var repo = _repositoryFactory.CreateServerRegistrationRepository(uow))
            {
                var query = Query<ServerRegistration>.Builder.Where(x => x.ComputerName.ToUpper() == computerName.ToUpper());
                var found = repo.GetByQuery(query).ToArray();
                ServerRegistration server;
                if (found.Any())
                {
                    server = found.First();
                    server.ServerAddress = address; //This should not  really change but it might!
                    server.UpdateDate = DateTime.UtcNow; //Stick with Utc dates since these might be globally distributed
                    server.IsActive = true;
                }
                else
                {
                    server = new ServerRegistration(address, computerName, DateTime.UtcNow)
                    {
                        IsActive = true
                    };
                }
                repo.AddOrUpdate(server);
                uow.Commit();
                repo.DeactiveStaleServers(staleServerTimeout);
            }
        }

        /// <summary>
        /// Deactivates a server by name
        /// </summary>
        /// <param name="computerName"></param>
        public void DeactiveServer(string computerName)
        {
            var uow = _uowProvider.GetUnitOfWork();
            using (var repo = _repositoryFactory.CreateServerRegistrationRepository(uow))
            {
                var query = Query<ServerRegistration>.Builder.Where(x => x.ComputerName.ToUpper() == computerName.ToUpper());
                var found = repo.GetByQuery(query).ToArray();
                if (found.Any())
                {
                    var server = found.First();
                    server.IsActive = false;
                    repo.AddOrUpdate(server);
                    uow.Commit();
                }
            }
        }

        public void DeactiveStaleServers(TimeSpan staleTimeout)
        {
            var uow = _uowProvider.GetUnitOfWork();
            using (var repo = _repositoryFactory.CreateServerRegistrationRepository(uow))
            {
                repo.DeactiveStaleServers(staleTimeout);
            }
        }

        /// <summary>
        /// Return all active servers
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ServerRegistration> GetActiveServers()
        {
            var uow = _uowProvider.GetUnitOfWork();
            using (var repo = _repositoryFactory.CreateServerRegistrationRepository(uow))
            {
                var query = Query<ServerRegistration>.Builder.Where(x => x.IsActive);
                return repo.GetByQuery(query).ToArray();
            }
        }
    }
}