using System;

namespace Umbraco.Core.Sync
{
    public sealed class DatabaseServerRegistrarOptions
    {
        public DatabaseServerRegistrarOptions()
        {
            //default is 1 day
            StaleServerTimeout = new TimeSpan(1,0,0);
            //60 seconds default
            ThrottleSeconds = 30;
        }

        /// <summary>
        /// The number of seconds to wait between previous 'call home' operations - this ensures that the call home operations
        /// are not performed too often.
        /// </summary>
        public int ThrottleSeconds { get; set; }

        public TimeSpan StaleServerTimeout { get; set; }
    }
}