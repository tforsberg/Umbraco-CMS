using System;
using System.Collections;
using System.Collections.Generic;

namespace Umbraco.Core.Sync
{
    public class DatabaseServerMessengerOptions
    {
        public DatabaseServerMessengerOptions()
        {
            DaysToRetainInstructionRecords = 100;
            ThrottleSeconds = 5;
        }

        /// <summary>
        /// A list of callbacks that will be invoked if the lastsynced.txt file does not exist
        /// </summary>
        /// <remarks>
        /// These callbacks will typically be for rebuilding the xml cache file and examine indexes based on the data in the database
        /// to get this particular server node up to date.
        /// </remarks>
        public IEnumerable<Action> RebuildingCallbacks { get; set; }

        /// <summary>
        /// The number of days to keep instructions in the db table, any records older than this number will be pruned.
        /// </summary>
        public int DaysToRetainInstructionRecords { get; set; }

        /// <summary>
        /// The number of seconds to wait between previous sync operations - this ensures that sync operations
        /// are not performed too often.
        /// </summary>
        public int ThrottleSeconds { get; set; }
    }
}