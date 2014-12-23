namespace Umbraco.Core.Sync
{
    internal class DatabaseServerMessengerOptions
    {
        public DatabaseServerMessengerOptions()
        {
            DaysToRetainInstructionRecords = 100;
            ThrottleSeconds = 5;
        }

        public int DaysToRetainInstructionRecords { get; set; }

        /// <summary>
        /// The number of seconds to wait between previous sync operations - this ensures that sync operations
        /// are not performed too often.
        /// </summary>
        public int ThrottleSeconds { get; set; }
    }
}