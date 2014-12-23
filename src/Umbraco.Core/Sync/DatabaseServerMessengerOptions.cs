namespace Umbraco.Core.Sync
{
    internal class DatabaseServerMessengerOptions
    {
        public DatabaseServerMessengerOptions()
        {
            DaysToRetainInstructionRecords = 100;
        }

        public int DaysToRetainInstructionRecords { get; set; }
    }
}