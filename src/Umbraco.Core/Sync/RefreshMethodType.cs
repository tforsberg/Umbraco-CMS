using System;

namespace Umbraco.Core.Sync
{
    [Serializable]
    public enum RefreshMethodType
    {
        RefreshAll,
        RefreshByGuid,
        RefreshById,
        RefreshByIds,
        RefreshByJson,
        RemoveById
    }
}