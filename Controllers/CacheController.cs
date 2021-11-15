using Newtonsoft.Json;
using Serilog;
using signals_hub.Interfaces;
using signals_hub.Models;
using signals_hub.Persistence;
using signals_hub.SignalsHubInterfaces;
using signals_hub.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace signals_hub.Controllers
{
    public class CacheController : Injectable
    {
        private static CacheController _instance = null;
        private IDataBaseQuery _database = null;
        private ISignalsHubOperations _operations = null;
        public ConcurrentDictionary<string, Signal> SignalDictionary;
        public ConcurrentDictionary<string, List<string>> DvcToSgnDictionary;
        public ConcurrentDictionary<string, Device> DeviceDictionary;

        private CacheController(IDataBaseQuery database, ISignalsHubOperations operations)
        {
            this._database = database;
            this._operations = operations;

            this.InitializeService();

            
        }

        public static CacheController Singleton(IDataBaseQuery database, ISignalsHubOperations operations)
        {
            if (_instance == null)
            {
                _instance = new CacheController(database, operations);
            }

            return _instance;
        }

        private async Task InitializeService()
        {
            await this.InitializeDatabaseAndInternalCacheMap();

            this._initializeSignalsRegistration();

            new Timer((arg) =>
            {
                new Timer((_args) => 
                {
                    InitializeDatabaseAndInternalCacheMap();
                }, "", 8640000, 86400000);
            }, null, 0, Timeout.Infinite);


        }

        private void _initializeSignalsRegistration()
        {
            List<Signal> l = SignalDictionary.Values.ToList();
            List<string> _signalsForRegistration = new List<string>();

            foreach (Signal s in l)
            {
                if (s.IsAlarm)
                {
                    _signalsForRegistration.Add(s.Id);
                }
            }
            

            foreach (KeyValuePair<string, Device> entry in DeviceDictionary)
            {
                this._operations.SubscribeToRealtimeSignal(_signalsForRegistration, entry.Value, ((string deviceId, List<(string signalId, string signalValue)> signalValues) callback) => {

                    return 0;
                });
            }
        }

        private async Task InitializeDatabaseAndInternalCacheMap()
        {
            await this.InitializeDevicesInDatabase();
            await this.InitializeSignalsInDatabase();
        }

        private async Task InitializeDevicesInDatabase()
        {
            // DEVICES
            // -------------------------------
            try
            {
                Dictionary<string, Device> _devicesInDatabase = new Dictionary<string, Device>();
                Dictionary<string, Device> _devicesInProvider = new Dictionary<string, Device>();
                List<(string parentIntId, string childIntId)> _devCompInDatabase = new List<(string parentIntId, string childIntId)>();
                List<(string parentIntId, string childIntId)> _devCompInProvider = new List<(string parentIntId, string childIntId)>();
                Dictionary<string, int> dvcColNames = new Dictionary<string, int>();
                Dictionary<string, int> devCompColNames = new Dictionary<string, int>();
                string resultString = null;
                // get devices from database
                string crudPayload = JSON_UTILS.PrepareCrudRequestPayload("devicesForm", "GNR", "Device", CRUD_EV_TYPE.READ);
                resultString = this._database.ExecuteCrud(crudPayload);
                dynamic resultJson = JsonConvert.DeserializeObject(resultString);
                // populate a local dictionary with devices read from database
                for (int i = 0; i < resultJson["body"]["table"][0]["colnames"].Count; i++)
                {
                    dvcColNames.Add(resultJson["body"]["table"][0]["colnames"][i].ToString(), i);
                }
                for (int i = 0; i < resultJson["body"]["table"][0]["data"]["invdata"].Count; i++)
                {
                    string guid = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["obj_uid"]];
                    string internalId = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sInternalId"]];
                    string id = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sId"]];
                    string name = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sName"]];
                    string description = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sDescription"]];
                    string commStatus = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sCommStatus"]];
                    string lastCommDate = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_dtLastCommDate"]];
                    string geoCoordinates = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sGeoCoordinates"]];
                    string formattedTags = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcColNames["dvc_sTags"]];
                    List<string> tags = formattedTags.Split("#").ToList<string>();

                    Device dvc = new Device(guid, internalId, id, name, description, commStatus, lastCommDate, null, geoCoordinates, null, null, tags);
                    _devicesInDatabase.Add(dvc.InternalId, dvc);
                }

                // get device composition from database
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("deviceCompositionForm", "GNR", "DeviceComposition", CRUD_EV_TYPE.READ);
                resultString = this._database.ExecuteCrud(crudPayload);
                resultJson = JsonConvert.DeserializeObject(resultString);
                // populate a local dictionary with device composition read from database
                for (int i = 0; i < resultJson["body"]["table"][0]["colnames"].Count; i++)
                {
                    devCompColNames.Add(resultJson["body"]["table"][0]["colnames"][i].ToString(), i);
                }
                for (int i = 0; i < resultJson["body"]["table"][0]["data"]["invdata"].Count; i++)
                {
                    string parentInternalId = resultJson["body"]["table"][0]["data"]["invdata"][i][devCompColNames["dcm_sParentInternalId"]];
                    string childInternalId = resultJson["body"]["table"][0]["data"]["invdata"][i][devCompColNames["dcm_sChildInternalId"]];

                    _devCompInDatabase.Add((parentInternalId, childInternalId));
                }

                // get devices and devices composition from provider
                (List<Device> devices, string message) resultFromProvider = await this._operations.ReadDevices();
                // populate a local dictionary with devices read from provider
                _devicesInProvider = resultFromProvider.devices.ToDictionary(device => device.InternalId, device => device);
                // populate a local dictionary with devices composition read from provider
                for (int i = 0; i < resultFromProvider.devices.Count; i++)
                {
                    for (int j = 0; j < resultFromProvider.devices[i].Children.Count; j++)
                        _devCompInProvider.Add((resultFromProvider.devices[i].InternalId, resultFromProvider.devices[i].Children[j].InternalId));
                }
                // compare databaseDictionary and providerDictionary:
                // if an item of providerDictionary is missing in databaseDictionary, add it in databaseDictionary and mark it with "create" label;
                // if an item of providerDictionary is already in databaseDictionary, update the existing item in databaseDictionary and mark it with "update" label;
                // if an item of databaseDictionary is missing in providerDictionary, mark the item in databaseDictionary with "delete" label

                // compare devices
                List<(string name, object value)> device;
                List<List<(string name, object value)>> createdDevices = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> updatedDevices = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> originalUpdatedDevices = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> deletedDevices = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> originalDeletedDevices = new List<List<(string name, object value)>>();
                foreach (KeyValuePair<string, Device> valuePair in _devicesInProvider)
                {
                    if (_devicesInDatabase.ContainsKey(valuePair.Key))
                    {
                        // new values
                        device = new List<(string name, object value)>();
                        device.Insert(dvcColNames["obj_uid"], ("obj_uid", _devicesInDatabase[valuePair.Key].Guid));
                        device.Insert(dvcColNames["dvc_sInternalId"], ("dvc_sInternalId", valuePair.Value.InternalId));
                        device.Insert(dvcColNames["dvc_sId"], ("dvc_sId", valuePair.Value.Id));
                        device.Insert(dvcColNames["dvc_sName"], ("dvc_sName", valuePair.Value.Name));
                        device.Insert(dvcColNames["dvc_sDescription"], ("dvc_sDescription", _devicesInDatabase[valuePair.Key].Description));
                        device.Insert(dvcColNames["dvc_sCommStatus"], ("dvc_sCommStatus", valuePair.Value.CommStatus));
                        device.Insert(dvcColNames["dvc_dtLastCommDate"], ("dvc_dtLastCommDate", valuePair.Value.LastCommDate));
                        device.Insert(dvcColNames["dvc_sGeoCoordinates"], ("dvc_sGeoCoordinates", valuePair.Value.GeoCoordinates));
                        string formattedTags = "#";
                        foreach (string tag in valuePair.Value.Tags)
                        {
                            formattedTags += tag + "#";
                        }
                        device.Insert(dvcColNames["dvc_sTags"], ("dvc_sTags", formattedTags));
                        device.Add(("dvc_bEnabled", 1));

                        updatedDevices.Add(device);

                        // original values
                        device = new List<(string name, object value)>();
                        device.Insert(dvcColNames["obj_uid"], ("obj_uid", _devicesInDatabase[valuePair.Key].Guid));
                        device.Insert(dvcColNames["dvc_sInternalId"], ("dvc_sInternalId", _devicesInDatabase[valuePair.Key].InternalId));
                        device.Insert(dvcColNames["dvc_sId"], ("dvc_sId", _devicesInDatabase[valuePair.Key].Id));
                        device.Insert(dvcColNames["dvc_sName"], ("dvc_sName", _devicesInDatabase[valuePair.Key].Name));
                        device.Insert(dvcColNames["dvc_sDescription"], ("dvc_sDescription", _devicesInDatabase[valuePair.Key].Description));
                        device.Insert(dvcColNames["dvc_sCommStatus"], ("dvc_sCommStatus", _devicesInDatabase[valuePair.Key].CommStatus));
                        device.Insert(dvcColNames["dvc_dtLastCommDate"], ("dvc_dtLastCommDate", _devicesInDatabase[valuePair.Key].LastCommDate));
                        device.Insert(dvcColNames["dvc_sGeoCoordinates"], ("dvc_sGeoCoordinates", _devicesInDatabase[valuePair.Key].GeoCoordinates));
                        formattedTags = "#";
                        foreach (string tag in _devicesInDatabase[valuePair.Key].Tags)
                        {
                            formattedTags += tag + "#";
                        }
                        device.Insert(dvcColNames["dvc_sTags"], ("dvc_sTags", formattedTags));
                        device.Add(("dvc_bEnabled", 1));

                        originalUpdatedDevices.Add(device);
                    }
                    else
                    {
                        device = new List<(string name, object value)>();
                        device.Insert(dvcColNames["obj_uid"], ("obj_uid", valuePair.Value.Guid));
                        device.Insert(dvcColNames["dvc_sInternalId"], ("dvc_sInternalId", valuePair.Value.InternalId));
                        device.Insert(dvcColNames["dvc_sId"], ("dvc_sId", valuePair.Value.Id));
                        device.Insert(dvcColNames["dvc_sName"], ("dvc_sName", valuePair.Value.Name));
                        device.Insert(dvcColNames["dvc_sDescription"], ("dvc_sDescription", valuePair.Value.Description));
                        device.Insert(dvcColNames["dvc_sCommStatus"], ("dvc_sCommStatus", valuePair.Value.CommStatus));
                        device.Insert(dvcColNames["dvc_dtLastCommDate"], ("dvc_dtLastCommDate", valuePair.Value.LastCommDate));
                        device.Insert(dvcColNames["dvc_sGeoCoordinates"], ("dvc_sGeoCoordinates", valuePair.Value.GeoCoordinates));
                        string formattedTags = "#";
                        foreach (string tag in valuePair.Value.Tags)
                        {
                            formattedTags += tag + "#";
                        }
                        device.Insert(dvcColNames["dvc_sTags"], ("dvc_sTags", formattedTags));
                        device.Add(("dvc_bEnabled", 1));

                        createdDevices.Add(device);
                    }
                }
                foreach (KeyValuePair<string, Device> valuePair in _devicesInDatabase)
                {
                    if (!_devicesInProvider.ContainsKey(valuePair.Key))
                    {
                        // new values
                        device = new List<(string name, object value)>();
                        device.Insert(dvcColNames["obj_uid"], ("obj_uid", valuePair.Value.Guid));
                        device.Insert(dvcColNames["dvc_sInternalId"], ("dvc_sInternalId", valuePair.Value.InternalId));
                        device.Insert(dvcColNames["dvc_sId"], ("dvc_sId", valuePair.Value.Id));
                        device.Insert(dvcColNames["dvc_sName"], ("dvc_sName", valuePair.Value.Name));
                        device.Insert(dvcColNames["dvc_sDescription"], ("dvc_sDescription", valuePair.Value.Description));
                        device.Insert(dvcColNames["dvc_sCommStatus"], ("dvc_sCommStatus", valuePair.Value.CommStatus));
                        device.Insert(dvcColNames["dvc_dtLastCommDate"], ("dvc_dtLastCommDate", valuePair.Value.LastCommDate));
                        device.Insert(dvcColNames["dvc_sGeoCoordinates"], ("dvc_sGeoCoordinates", valuePair.Value.GeoCoordinates));
                        string formattedTags = "#";
                        foreach (string tag in valuePair.Value.Tags)
                        {
                            formattedTags += tag + "#";
                        }
                        device.Insert(dvcColNames["dvc_sTags"], ("dvc_sTags", formattedTags));
                        device.Add(("dvc_bEnabled", 0));

                        deletedDevices.Add(device);

                        // original values
                        device = new List<(string name, object value)>();
                        device.Insert(dvcColNames["obj_uid"], ("obj_uid", valuePair.Value.Guid));
                        device.Insert(dvcColNames["dvc_sInternalId"], ("dvc_sInternalId", valuePair.Value.InternalId));
                        device.Insert(dvcColNames["dvc_sId"], ("dvc_sId", valuePair.Value.Id));
                        device.Insert(dvcColNames["dvc_sName"], ("dvc_sName", valuePair.Value.Name));
                        device.Insert(dvcColNames["dvc_sDescription"], ("dvc_sDescription", valuePair.Value.Description));
                        device.Insert(dvcColNames["dvc_sCommStatus"], ("dvc_sCommStatus", valuePair.Value.CommStatus));
                        device.Insert(dvcColNames["dvc_dtLastCommDate"], ("dvc_dtLastCommDate", valuePair.Value.LastCommDate));
                        device.Insert(dvcColNames["dvc_sGeoCoordinates"], ("dvc_sGeoCoordinates", valuePair.Value.GeoCoordinates));
                        formattedTags = "#";
                        foreach (string tag in valuePair.Value.Tags)
                        {
                            formattedTags += tag + "#";
                        }
                        device.Insert(dvcColNames["dvc_sTags"], ("dvc_sTags", formattedTags));
                        device.Add(("dvc_bEnabled", 1));

                        originalDeletedDevices.Add(device);
                    }
                }

                // compare devices composition
                List<(string name, object value)> dvcComp;
                List<List<(string name, object value)>> createdDvcComp = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> deletedDvcComp = new List<List<(string name, object value)>>();
                bool found;
                for (int i = 0; i < _devCompInProvider.Count; i++)
                {
                    found = false;
                    for (int j = 0; j < _devCompInDatabase.Count && !found; j++)
                    {
                        if (_devCompInProvider[i].parentIntId.Equals(_devCompInDatabase[j].parentIntId)
                            && _devCompInProvider[i].childIntId.Equals(_devCompInDatabase[j].childIntId))
                        {
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        dvcComp = new List<(string name, object value)>();
                        dvcComp.Insert(devCompColNames["dcm_sParentInternalId"], ("dcm_sParentInternalId", _devCompInProvider[i].parentIntId));
                        dvcComp.Insert(devCompColNames["dcm_sChildInternalId"], ("dcm_sChildInternalId", _devCompInProvider[i].childIntId));

                        createdDvcComp.Add(dvcComp);
                    }
                }
                for (int i = 0; i < _devCompInDatabase.Count; i++)
                {
                    found = false;
                    for (int j = 0; j < _devCompInProvider.Count && !found; j++)
                    {
                        if (_devCompInDatabase[i].parentIntId.Equals(_devCompInProvider[j].parentIntId)
                            && _devCompInDatabase[i].childIntId.Equals(_devCompInProvider[j].childIntId))
                        {
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        dvcComp = new List<(string name, object value)>();
                        dvcComp.Insert(devCompColNames["dcm_sParentInternalId"], ("dcm_sParentInternalId", _devCompInDatabase[i].parentIntId));
                        dvcComp.Insert(devCompColNames["dcm_sChildInternalId"], ("dcm_sChildInternalId", _devCompInDatabase[i].childIntId));

                        deletedDvcComp.Add(dvcComp);
                    }
                }

                // UPDATING DEVICES IN DATABASE
                // CREATE
                (string s, object o)[][] createdDvcMatrix = new (string s, object o)[createdDevices.Count][];
                for (int i = 0; i < createdDevices.Count; i++)
                {
                    createdDvcMatrix[i] = createdDevices[i].ToArray();
                }

                if (createdDvcMatrix.Length > 0)
                {
                    crudPayload = JSON_UTILS.PrepareCrudRequestPayload("devicesForm", "GNR", "Device", CRUD_EV_TYPE.CREATE, createdDvcMatrix);
                    resultString = this._database.ExecuteCrud(crudPayload);
                }
                // UPDATE
                (string s, object o)[][] updatedDvcMatrix = new (string s, object o)[updatedDevices.Count][];
                (string s, object o)[][] originalUpdatedDvcMatrix = new (string s, object o)[originalUpdatedDevices.Count][];
                for (int i = 0; i < updatedDevices.Count; i++)
                {
                    updatedDvcMatrix[i] = updatedDevices[i].ToArray();
                    originalUpdatedDvcMatrix[i] = originalUpdatedDevices[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("devicesForm", "GNR", "Device", CRUD_EV_TYPE.UPDATE, updatedDvcMatrix, originalUpdatedDvcMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);
                // DELETE
                (string s, object o)[][] deletedDvcMatrix = new (string s, object o)[deletedDevices.Count][];
                (string s, object o)[][] originalDeletedDvcMatrix = new (string s, object o)[originalDeletedDevices.Count][];
                for (int i = 0; i < deletedDevices.Count; i++)
                {
                    deletedDvcMatrix[i] = deletedDevices[i].ToArray();
                    originalDeletedDvcMatrix[i] = originalDeletedDevices[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("devicesForm", "GNR", "Device", CRUD_EV_TYPE.UPDATE, deletedDvcMatrix, originalDeletedDvcMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);

                // UPDATING DEVICES COMPOSITION IN DATABASE
                // CREATE
                (string s, object o)[][] createdDvcCompMatrix = new (string s, object o)[createdDvcComp.Count][];
                for (int i = 0; i < createdDvcComp.Count; i++)
                {
                    createdDvcCompMatrix[i] = createdDvcComp[i].ToArray();
                }

                if (createdDvcCompMatrix.Length > 0)
                {
                    crudPayload = JSON_UTILS.PrepareCrudRequestPayload("deviceCompositionForm", "GNR", "DeviceComposition", CRUD_EV_TYPE.CREATE, createdDvcCompMatrix);
                    resultString = this._database.ExecuteCrud(crudPayload);
                }
                // DELETE
                (string s, object o)[][] deletedDvcCompMatrix = new (string s, object o)[deletedDvcComp.Count][];
                for (int i = 0; i < deletedDvcComp.Count; i++)
                {
                    deletedDvcCompMatrix[i] = deletedDvcComp[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("signalsForm", "GNR", "Signal", CRUD_EV_TYPE.DELETE, deletedDvcCompMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);

                // refresh cached devices list
                this.DeviceDictionary = new ConcurrentDictionary<string, Device>();
                Device d;
                foreach (List<(string name, object value)> el in createdDevices)
                {
                    string guid = (string)el[dvcColNames["obj_uid"]].value;
                    string internalId = (string)el[dvcColNames["dvc_sInternalId"]].value;
                    string id = (string)el[dvcColNames["dvc_sId"]].value;
                    string name = (string)el[dvcColNames["dvc_sName"]].value;
                    string description = (string)el[dvcColNames["dvc_sDescription"]].value;
                    string commStatus = (string)el[dvcColNames["dvc_sCommStatus"]].value;
                    string lastCommDate = el[dvcColNames["dvc_dtLastCommDate"]].value.ToString();
                    string geoCoordinates = (string)el[dvcColNames["dvc_sGeoCoordinates"]].value;
                    string formattedTags = (string)el[dvcColNames["dvc_sTags"]].value;
                    List<string> tags = formattedTags.Split("#").ToList<string>();

                    d = new Device(guid, internalId, id, name, description, commStatus, lastCommDate, null, geoCoordinates, null, null, tags);
                    this.DeviceDictionary.TryAdd(d.InternalId, d);
                }
                foreach (List<(string name, object value)> el in updatedDevices)
                {
                    string guid = (string)el[dvcColNames["obj_uid"]].value;
                    string internalId = (string)el[dvcColNames["dvc_sInternalId"]].value;
                    string id = (string)el[dvcColNames["dvc_sId"]].value;
                    string name = (string)el[dvcColNames["dvc_sName"]].value;
                    string description = (string)el[dvcColNames["dvc_sDescription"]].value;
                    string commStatus = (string)el[dvcColNames["dvc_sCommStatus"]].value;
                    string lastCommDate = el[dvcColNames["dvc_dtLastCommDate"]].value.ToString();
                    string geoCoordinates = (string)el[dvcColNames["dvc_sGeoCoordinates"]].value;
                    string formattedTags = (string)el[dvcColNames["dvc_sTags"]].value;
                    List<string> tags = formattedTags.Split("#").ToList<string>();

                    d = new Device(guid, internalId, id, name, description, commStatus, lastCommDate, null, geoCoordinates, null, null, tags);
                    this.DeviceDictionary.TryAdd(d.InternalId, d);
                }
            }
            catch(Exception e)
            {
                Log.Error(e, "CacheController.InitializeDevicesInDatabase >>> Error during initialization of devices. Cache cannot be created");
            }
            
        }

        private async Task InitializeSignalsInDatabase()
        {
            try
            {
                // SIGNALS
                // -------------------------------
                Dictionary<string, Signal> _signalsInDatabase = new Dictionary<string, Signal>();
                Dictionary<string, Signal> _signalsInProvider = new Dictionary<string, Signal>();
                Dictionary<string, (string dvcIntId, string sgnId, string rlIntId, string stgIntId, string wkfIntId, string priority)> _sgnToDvcInDatabase =
                    new Dictionary<string, (string dvcIntId, string sgnId, string rlIntId, string stgIntId, string wkfIntId, string priority)>();
                Dictionary<string, (string dvcIntId, string sgnId)> _sgnToDvcInProvider = new Dictionary<string, (string, string)>();
                Dictionary<string, int> sgnColNames = new Dictionary<string, int>();
                Dictionary<string, int> dvcToWkfColNames = new Dictionary<string, int>();
                string resultString = null;
                // get signals from database
                string crudPayload = JSON_UTILS.PrepareCrudRequestPayload("signalsForm", "GNR", "Signal", CRUD_EV_TYPE.READ);
                resultString = this._database.ExecuteCrud(crudPayload);
                dynamic resultJson = JsonConvert.DeserializeObject(resultString);
                // populate a local dictionary with signals read from database
                for (int i = 0; i < resultJson["body"]["table"][0]["colnames"].Count; i++)
                {
                    sgnColNames.Add(resultJson["body"]["table"][0]["colnames"][i].ToString(), i);
                }
                for (int i = 0; i < resultJson["body"]["table"][0]["data"]["invdata"].Count; i++)
                {
                    string guid = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["obj_uid"]];
                    string internalId = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_sInternalId"]];
                    string id = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_sId"]];
                    string name = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_sName"]];
                    string description = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_sDescription"]];
                    string unit = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_sUnit"]];
                    bool readable = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_bReadable"]] == "1" ? true : false;
                    bool writable = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_bWritable"]] == "1" ? true : false;
                    bool isAlarm = resultJson["body"]["table"][0]["data"]["invdata"][i][sgnColNames["sgn_bIsAlarm"]] == "1" ? true : false;

                    Signal sgn = new Signal(guid, internalId, id, name, description, unit, readable, writable, isAlarm, null);
                    _signalsInDatabase.Add(sgn.InternalId, sgn);
                }

                // get DeviceToWorkflow records from database for device-signal association
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("deviceToWorkflowForm", "GNR", "DeviceToWorkflow", CRUD_EV_TYPE.READ);
                resultString = this._database.ExecuteCrud(crudPayload);
                resultJson = JsonConvert.DeserializeObject(resultString);
                // populate a local dictionary with DeviceToWorkflow records read from database
                for (int i = 0; i < resultJson["body"]["table"][0]["colnames"].Count; i++)
                {
                    dvcToWkfColNames.Add(resultJson["body"]["table"][0]["colnames"][i].ToString(), i);
                }
                for (int i = 0; i < resultJson["body"]["table"][0]["data"]["invdata"].Count; i++)
                {
                    string dvcIntId = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcToWkfColNames["dtw_sDeviceInternalId"]];
                    string sgnId = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcToWkfColNames["dtw_sSignalId"]];
                    string rlIntId = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcToWkfColNames["dtw_sRuleInternalId"]];
                    string stgIntId = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcToWkfColNames["dtw_sStageInternalId"]];
                    string wkfIntId = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcToWkfColNames["dtw_sWkfInternalId"]];
                    string priority = resultJson["body"]["table"][0]["data"]["invdata"][i][dvcToWkfColNames["dtw_sPriority"]];

                    _sgnToDvcInDatabase.Add(String.Format("{0}$$${1}", dvcIntId, sgnId), (dvcIntId, sgnId, rlIntId, stgIntId, wkfIntId, priority));
                }

                // get signals and device-signal associations from provider
                (List<Signal> signals, List<string> dvcIntIds, string message) resultFromProvider = await this._operations.ReadSignals();
                // populate a local dictionary with signals read from provider
                foreach (Signal sig in resultFromProvider.signals)
                {
                    _signalsInProvider.TryAdd(sig.Id, sig);
                }

                // using InternalId instead of id as key
                for (int i = 0; i < _signalsInProvider.Count; i++)
                {
                    Signal sig = _signalsInProvider.ElementAt(i).Value;
                    _signalsInProvider.Remove(_signalsInProvider.ElementAt(i).Key);
                    _signalsInProvider.Add(sig.InternalId, sig);
                }
                // populate a local dictionary with device-signal associations read from provider
                for (int i = 0; i < resultFromProvider.signals.Count; i++)
                {
                    _sgnToDvcInProvider.Add(
                        String.Format("{0}$$${1}", resultFromProvider.dvcIntIds[i], resultFromProvider.signals[i].Id),
                        (resultFromProvider.dvcIntIds[i], resultFromProvider.signals[i].Id)
                    );
                }
                // compare databaseDictionary and providerDictionary:
                // if an item of providerDictionary is missing in databaseDictionary, add it in databaseDictionary and mark it with "create" label;
                // if an item of providerDictionary is already in databaseDictionary, update the existing item in databaseDictionary and mark it with "update" label;
                // if an item of databaseDictionary is missing in providerDictionary, mark the item in databaseDictionary with "delete" label

                // comparison for signals
                List<(string name, object value)> signal;
                List<List<(string name, object value)>> createdSignals = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> updatedSignals = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> originalSignals = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> deletedSignals = new List<List<(string name, object value)>>();
                foreach (KeyValuePair<string, Signal> valuePair in _signalsInProvider)
                {
                    if (_signalsInDatabase.ContainsKey(valuePair.Key))
                    {
                        //new values
                        signal = new List<(string name, object value)>();
                        signal.Insert(sgnColNames["obj_uid"], ("obj_uid", _signalsInDatabase[valuePair.Key].Guid));
                        signal.Insert(sgnColNames["sgn_sInternalId"], ("sgn_sInternalId", valuePair.Value.InternalId));
                        signal.Insert(sgnColNames["sgn_sId"], ("sgn_sId", valuePair.Value.Id));
                        signal.Insert(sgnColNames["sgn_sName"], ("sgn_sName", valuePair.Value.Name));
                        signal.Insert(sgnColNames["sgn_sDescription"], ("sgn_sDescription", _signalsInDatabase[valuePair.Key].Description));
                        signal.Insert(sgnColNames["sgn_sUnit"], ("sgn_sUnit", valuePair.Value.Unit));
                        signal.Insert(sgnColNames["sgn_bReadable"], ("sgn_bReadable", valuePair.Value.Readable));
                        signal.Insert(sgnColNames["sgn_bWritable"], ("sgn_bWritable", valuePair.Value.Writable));
                        signal.Insert(sgnColNames["sgn_bIsAlarm"], ("sgn_bIsAlarm", valuePair.Value.IsAlarm));

                        updatedSignals.Add(signal);

                        //original values
                        signal = new List<(string name, object value)>();
                        signal.Insert(sgnColNames["obj_uid"], ("obj_uid", _signalsInDatabase[valuePair.Key].Guid));
                        signal.Insert(sgnColNames["sgn_sInternalId"], ("sgn_sInternalId", _signalsInDatabase[valuePair.Key].InternalId));
                        signal.Insert(sgnColNames["sgn_sId"], ("sgn_sId", _signalsInDatabase[valuePair.Key].Id));
                        signal.Insert(sgnColNames["sgn_sName"], ("sgn_sName", _signalsInDatabase[valuePair.Key].Name));
                        signal.Insert(sgnColNames["sgn_sDescription"], ("sgn_sDescription", _signalsInDatabase[valuePair.Key].Description));
                        signal.Insert(sgnColNames["sgn_sUnit"], ("sgn_sUnit", _signalsInDatabase[valuePair.Key].Unit));
                        signal.Insert(sgnColNames["sgn_bReadable"], ("sgn_bReadable", _signalsInDatabase[valuePair.Key].Readable));
                        signal.Insert(sgnColNames["sgn_bWritable"], ("sgn_bWritable", _signalsInDatabase[valuePair.Key].Writable));
                        signal.Insert(sgnColNames["sgn_bIsAlarm"], ("sgn_bIsAlarm", _signalsInDatabase[valuePair.Key].IsAlarm));

                        originalSignals.Add(signal);
                    }
                    else
                    {
                        signal = new List<(string name, object value)>();
                        signal.Insert(sgnColNames["obj_uid"], ("obj_uid", valuePair.Value.Guid));
                        signal.Insert(sgnColNames["sgn_sInternalId"], ("sgn_sInternalId", valuePair.Value.InternalId));
                        signal.Insert(sgnColNames["sgn_sId"], ("sgn_sId", valuePair.Value.Id));
                        signal.Insert(sgnColNames["sgn_sName"], ("sgn_sName", valuePair.Value.Name));
                        signal.Insert(sgnColNames["sgn_sDescription"], ("sgn_sDescription", valuePair.Value.Description));
                        signal.Insert(sgnColNames["sgn_sUnit"], ("sgn_sUnit", valuePair.Value.Unit));
                        signal.Insert(sgnColNames["sgn_bReadable"], ("sgn_bReadable", valuePair.Value.Readable));
                        signal.Insert(sgnColNames["sgn_bWritable"], ("sgn_bWritable", valuePair.Value.Writable));
                        signal.Insert(sgnColNames["sgn_bIsAlarm"], ("sgn_bIsAlarm", valuePair.Value.IsAlarm));

                        createdSignals.Add(signal);
                    }
                }
                foreach (KeyValuePair<string, Signal> valuePair in _signalsInDatabase)
                {
                    if (!_signalsInProvider.ContainsKey(valuePair.Key))
                    {
                        signal = new List<(string name, object value)>();
                        signal.Insert(sgnColNames["sgn_sInternalId"], ("sgn_sInternalId", valuePair.Value.InternalId));

                        deletedSignals.Add(signal);
                    }
                }

                // comparison for device-signal associations
                List<(string name, object value)> dvcToWkf;
                List<List<(string name, object value)>> createdDvcToWkf = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> updatedDvcToWkf = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> originalDvcToWkfs = new List<List<(string name, object value)>>();
                List<List<(string name, object value)>> deletedDvcToWkf = new List<List<(string name, object value)>>();
                foreach (KeyValuePair<string, (string dvcIntId, string sgnId)> valuePair in _sgnToDvcInProvider)
                {
                    if (_sgnToDvcInDatabase.ContainsKey(valuePair.Key))
                    {
                        // new values
                        dvcToWkf = new List<(string name, object value)>();
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sDeviceInternalId"], ("dtw_sDeviceInternalId", valuePair.Value.dvcIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sSignalId"], ("dtw_sSignalId", valuePair.Value.sgnId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sRuleInternalId"], ("dtw_sRuleInternalId", _sgnToDvcInDatabase[valuePair.Key].rlIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sStageInternalId"], ("dtw_sStageInternalId", _sgnToDvcInDatabase[valuePair.Key].stgIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sWkfInternalId"], ("dtw_sWkfInternalId", _sgnToDvcInDatabase[valuePair.Key].wkfIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sPriority"], ("dtw_sPriority", _sgnToDvcInDatabase[valuePair.Key].priority));

                        updatedDvcToWkf.Add(dvcToWkf);

                        // original values
                        dvcToWkf = new List<(string name, object value)>();
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sDeviceInternalId"], ("dtw_sDeviceInternalId", _sgnToDvcInDatabase[valuePair.Key].dvcIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sSignalId"], ("dtw_sSignalId", _sgnToDvcInDatabase[valuePair.Key].sgnId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sRuleInternalId"], ("dtw_sRuleInternalId", _sgnToDvcInDatabase[valuePair.Key].rlIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sStageInternalId"], ("dtw_sStageInternalId", _sgnToDvcInDatabase[valuePair.Key].stgIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sWkfInternalId"], ("dtw_sWkfInternalId", _sgnToDvcInDatabase[valuePair.Key].wkfIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sPriority"], ("dtw_sPriority", _sgnToDvcInDatabase[valuePair.Key].priority));

                        originalDvcToWkfs.Add(dvcToWkf);
                    }
                    else
                    {
                        dvcToWkf = new List<(string name, object value)>();
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sDeviceInternalId"], ("dtw_sDeviceInternalId", valuePair.Value.dvcIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sSignalId"], ("dtw_sSignalId", valuePair.Value.sgnId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sRuleInternalId"], ("dtw_sRuleInternalId", ""));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sStageInternalId"], ("dtw_sStageInternalId", ""));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sWkfInternalId"], ("dtw_sWkfInternalId", ""));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sPriority"], ("dtw_sPriority", ""));

                        createdDvcToWkf.Add(dvcToWkf);
                    }
                }
                foreach (KeyValuePair<string, (string dvcIntId, string sgnId, string rlIntId, string stgIntId, string wkfIntId, string priority)> valuePair in _sgnToDvcInDatabase)
                {
                    if (!_sgnToDvcInProvider.ContainsKey(valuePair.Key))
                    {
                        dvcToWkf = new List<(string name, object value)>();
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sDeviceInternalId"], ("dtw_sDeviceInternalId", valuePair.Value.dvcIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sSignalId"], ("dtw_sSignalId", valuePair.Value.sgnId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sRuleInternalId"], ("dtw_sRuleInternalId", valuePair.Value.rlIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sStageInternalId"], ("dtw_sStageInternalId", valuePair.Value.stgIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sWkfInternalId"], ("dtw_sWkfInternalId", valuePair.Value.wkfIntId));
                        dvcToWkf.Insert(dvcToWkfColNames["dtw_sPriority"], ("dtw_sPriority", valuePair.Value.priority));

                        deletedDvcToWkf.Add(dvcToWkf);
                    }
                }

                // UPDATING SIGNALS IN DATABASE
                // CREATE
                (string s, object o)[][] createdSgnMatrix = new (string s, object o)[createdSignals.Count][];
                for (int i = 0; i < createdSignals.Count; i++)
                {
                    createdSgnMatrix[i] = createdSignals[i].ToArray();
                }
                if (createdSgnMatrix.Length > 0)
                {
                    crudPayload = JSON_UTILS.PrepareCrudRequestPayload("signalsForm", "GNR", "Signal", CRUD_EV_TYPE.CREATE, createdSgnMatrix);
                    resultString = this._database.ExecuteCrud(crudPayload);
                }
                // UPDATE
                (string s, object o)[][] updatedSgnMatrix = new (string s, object o)[updatedSignals.Count][];
                (string s, object o)[][] originalSgnMatrix = new (string s, object o)[originalSignals.Count][];
                for (int i = 0; i < updatedSignals.Count; i++)
                {
                    updatedSgnMatrix[i] = updatedSignals[i].ToArray();
                    originalSgnMatrix[i] = originalSignals[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("signalsForm", "GNR", "Signal", CRUD_EV_TYPE.UPDATE, updatedSgnMatrix, originalSgnMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);
                // DELETE
                (string s, object o)[][] deletedSgnMatrix = new (string s, object o)[deletedSignals.Count][];
                for (int i = 0; i < deletedSignals.Count; i++)
                {
                    deletedSgnMatrix[i] = deletedSignals[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("signalsForm", "GNR", "Signal", CRUD_EV_TYPE.DELETE, deletedSgnMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);

                // UPDATING DEVICE-SIGNAL ASSOCIATIONS IN DATABASE
                // CREATE -----------------> NOT WORKING (foreign key constraint violated)
                (string s, object o)[][] createdDvcToWkfMatrix = new (string s, object o)[createdDvcToWkf.Count][];
                for (int i = 0; i < createdDvcToWkf.Count; i++)
                {
                    createdDvcToWkfMatrix[i] = createdDvcToWkf[i].ToArray();
                }
                if (createdDvcToWkfMatrix.Length > 0)
                {
                    crudPayload = JSON_UTILS.PrepareCrudRequestPayload("deviceToWorkflowForm", "GNR", "DeviceToWorkflow", CRUD_EV_TYPE.CREATE, createdDvcToWkfMatrix);
                    resultString = this._database.ExecuteCrud(crudPayload);
                }
                // UPDATE
                (string s, object o)[][] updatedDvcToWkfMatrix = new (string s, object o)[updatedDvcToWkf.Count][];
                (string s, object o)[][] originalDvcToWkfMatrix = new (string s, object o)[originalDvcToWkfs.Count][];
                for (int i = 0; i < updatedDvcToWkf.Count; i++)
                {
                    updatedDvcToWkfMatrix[i] = updatedDvcToWkf[i].ToArray();
                    originalDvcToWkfMatrix[i] = originalDvcToWkfs[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("deviceToWorkflowForm", "GNR", "DeviceToWorkflow", CRUD_EV_TYPE.UPDATE, updatedDvcToWkfMatrix, originalDvcToWkfMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);
                // DELETE
                (string s, object o)[][] deletedDvcToWkfMatrix = new (string s, object o)[deletedDvcToWkf.Count][];
                for (int i = 0; i < deletedDvcToWkf.Count; i++)
                {
                    deletedDvcToWkfMatrix[i] = deletedDvcToWkf[i].ToArray();
                }
                crudPayload = JSON_UTILS.PrepareCrudRequestPayload("deviceToWorkflowForm", "GNR", "DeviceToWorkflow", CRUD_EV_TYPE.DELETE, deletedDvcToWkfMatrix);
                resultString = this._database.ExecuteCrud(crudPayload);

                // refresh cached signals list
                this.SignalDictionary = new ConcurrentDictionary<string, Signal>();
                Signal s;
                foreach (List<(string name, object value)> el in createdSignals)
                {
                    string guid = (string)el[sgnColNames["obj_uid"]].value;
                    string internalId = (string)el[sgnColNames["sgn_sInternalId"]].value;
                    string id = (string)el[sgnColNames["sgn_sId"]].value;
                    string name = (string)el[sgnColNames["sgn_sName"]].value;
                    string description = (string)el[sgnColNames["sgn_sDescription"]].value;
                    string unit = (string)el[sgnColNames["sgn_sUnit"]].value;
                    bool readable = (bool)el[sgnColNames["sgn_bReadable"]].value;
                    bool writable = (bool)el[sgnColNames["sgn_bWritable"]].value;
                    bool isAlarm = (bool)el[sgnColNames["sgn_bIsAlarm"]].value;

                    s = new Signal(guid, internalId, id, name, description, unit, readable, writable, isAlarm, null);
                    this.SignalDictionary.TryAdd(s.InternalId, s);
                }
                foreach (List<(string name, object value)> el in updatedSignals)
                {
                    string guid = (string)el[sgnColNames["obj_uid"]].value;
                    string internalId = (string)el[sgnColNames["sgn_sInternalId"]].value;
                    string id = (string)el[sgnColNames["sgn_sId"]].value;
                    string name = (string)el[sgnColNames["sgn_sName"]].value;
                    string description = (string)el[sgnColNames["sgn_sDescription"]].value;
                    string unit = (string)el[sgnColNames["sgn_sUnit"]].value;
                    bool readable = (bool)el[sgnColNames["sgn_bReadable"]].value;
                    bool writable = (bool)el[sgnColNames["sgn_bWritable"]].value;
                    bool isAlarm = (bool)el[sgnColNames["sgn_bIsAlarm"]].value;

                    s = new Signal(guid, internalId, id, name, description, unit, readable, writable, isAlarm, null);
                    this.SignalDictionary.TryAdd(s.InternalId, s);
                }

                // refresh cached signal-device associacions list
                this.DvcToSgnDictionary = new ConcurrentDictionary<string, List<string>>();
                foreach (List<(string name, object value)> el in createdDvcToWkf)
                {
                    string dvcIntId = (string)el[dvcToWkfColNames["dtw_sDeviceInternalId"]].value;
                    string sgnId = (string)el[dvcToWkfColNames["dtw_sSignalId"]].value;
                    List<string> sgnList = new List<string>();
                    sgnList.Add(sgnId);
                    if (!this.DvcToSgnDictionary.TryAdd(dvcIntId, sgnList))
                    {
                        this.DvcToSgnDictionary[dvcIntId].Add(sgnId);
                    }
                }
                foreach (List<(string name, object value)> el in updatedDvcToWkf)
                {
                    string dvcIntId = (string)el[dvcToWkfColNames["dtw_sDeviceInternalId"]].value;
                    string sgnId = (string)el[dvcToWkfColNames["dtw_sSignalId"]].value;
                    List<string> sgnList = new List<string>();
                    sgnList.Add(sgnId);
                    if (!this.DvcToSgnDictionary.TryAdd(dvcIntId, sgnList))
                    {
                        this.DvcToSgnDictionary[dvcIntId].Add(sgnId);
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e, "CacheController.InitializeSignalsInDatabase >>> error in initialization of signals cache and db update");
            }
        }
    }
}
