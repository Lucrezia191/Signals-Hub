namespace signals_hub.SignalsHubInterfaces.SignalsHubDBoomImpl
{
    public static class DBOOM_RESTAPI
    {
        public static readonly string GET_DEVICES_ALL = "/devices/all";
        public static readonly string GET_DEVICE_BY_ID = "/devices/@deviceId?populatePermission=true";
        public static readonly string GET_DEVICE_BY_TOKEN = "/devices/token/@tokenId";
        public static readonly string GET_SIGNALS_WITH_PAGINATION = "/signals/?page=@pageNumber&per_page=100";
        public static readonly string GET_SIGNAL_BY_ID = "/signals/@signalId?populatePermission=true&populateDevice=true";
        public static readonly string CREATE_DEVICE = "/devices/";
        public static readonly string UPDATE_DEVICE = "/devices/@deviceId";
        public static readonly string DELETE_DEVICE = "/devices/@deviceId";
        public static readonly string CREATE_SIGNAL = "/signals/";
        public static readonly string UPDATE_SIGNAL = "/signals/@signalId";
        public static readonly string DELETE_SIGNAL = "/signals/@signalId";
        public static readonly string POST_READ_HISTORICAL_FOR_SIGNAL = "/chart";
    }
}