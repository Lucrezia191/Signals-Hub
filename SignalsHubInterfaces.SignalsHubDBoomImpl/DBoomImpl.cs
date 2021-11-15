using Newtonsoft.Json;
using Serilog;
using signals_hub.Controllers;
using signals_hub.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace signals_hub.SignalsHubInterfaces.SignalsHubDBoomImpl
{
    public class DBoomImpl : ISignalsHubOperations
    {
        private static DBoomImpl _instance = null;
        private string _serviceBaseUrl = null;
        private string _apikey = null;
        private ConcurrentDictionary<string, (Func<(string deviceId, List<(string signalId, string signalValue)> signalValues), int> callback, List<string> registeredSignals)> _registeredSignals
            = new ConcurrentDictionary<string, (Func<(string deviceId, List<(string signalId, string signalValue)> signalValues), int> callback, List<string> registeredSignals)>();
        private ConcurrentDictionary<string, string> _mqttRegisteredDevices = new ConcurrentDictionary<string, string>();

        private ConcurrentDictionary<string, ConcurrentDictionary<string, Signal>> _DBoomDeviceToSignalRel = new ConcurrentDictionary<string, ConcurrentDictionary<string, Signal>>();

        private DBoomImpl()
        {
            InitControllerParameter parameter = null;
            // get URL
            InitController.Singleton().EParams.TryGetValue("SIGNULHUB_PROVIDER_BASEURL", out parameter);
            this._serviceBaseUrl = parameter.Value;

            // get api key
            InitController.Singleton().EParams.TryGetValue("DBOOMAPIKEY", out parameter);
            this._apikey = parameter.Value;
        }

        public static DBoomImpl Singleton()
        {
            if(_instance == null)
            {
                _instance = new DBoomImpl();
            }
            return _instance;
        }

        private void _LogApiCall(HttpWebRequest _webReq, string jsonPayload = "null")
        {
            Log.Verbose(string.Format("ApiRequestService.LogApiCall", "\n\n---- URL: {0}\n\n---- Pay: {1}", _webReq.Address, jsonPayload));
        }

        private void _LogApiResponse(HttpWebRequest _webReq, string jsonResponse)
        {
            Log.Verbose(string.Format("ApiRequestService.LogApiResponse", "\n\nURL: {0}\n\nResponse: \n\n{1}", _webReq.Address, jsonResponse));
        }

        public async Task<(Device device, string message)> DeleteDevice(Device device)
        {
            (Device device, string message) _result = (null, null);
            try
            {
                string url = this._serviceBaseUrl + DBOOM_RESTAPI.DELETE_DEVICE;
                url.Replace("@deviceId", device.InternalId);
                HttpWebRequest webReq = _CreateWebRequest(HTTP_METHOD.DELETE, url);
                // log request
                _LogApiCall(webReq);
                HttpWebResponse webResponse = (HttpWebResponse)webReq.GetResponse();
                StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                string result = await reader.ReadToEndAsync();
                // log response
                _LogApiResponse(webReq, result);
                dynamic resultObj = JsonConvert.DeserializeObject(result);
                string rId = resultObj["device_token"];
                string rName = resultObj["description"];
                string rCommStatus = resultObj["com_status"];
                if (rCommStatus.Equals("INIT") || rCommStatus.Equals("ACTIVE"))
                {
                    rCommStatus = Enum.GetName(typeof(DEVICE_STATUS), DEVICE_STATUS.ACTIVE);
                }
                else
                {
                    rCommStatus = Enum.GetName(typeof(DEVICE_STATUS), DEVICE_STATUS.UNREACHABLE);
                }
                string rlastCommDateISOString = resultObj["lastCommDate"];
                List<Signal> rSignals = new List<Signal>();
                for (int i = 0; i < resultObj["signals"].Count; i++)
                {
                    string rSigIntId = resultObj["signals"][i];
                    rSignals.Add(new Signal("", rSigIntId, ""));
                }
                string rGeoCoordinates = resultObj["geoCoordinates"]["lat"] + ";" + resultObj["geoCoordinates"]["lng"];
                List<string> rTags = new List<string>();
                for (int i = 0; i < resultObj["tags"].Count; i++)
                {
                    string rTag = resultObj["tags"][i];
                    rTags.Add(rTag);
                }
                _result.device = new Device(device.Guid, device.InternalId, rId, rName, device.Description, rCommStatus, rlastCommDateISOString, rSignals, rGeoCoordinates, device.Parent, device.Children, rTags);
                _result.message = null;
            }
            catch (Exception e)
            {
                Log.Error(string.Format("DBoomImpl.DeleteDevice >>> Error in deleting device. error: {0} \n stack trace \n {1}", e.Message, e.StackTrace));
            }
            return _result;
        }

        public async Task<(Signal signal, string message)> DeleteSignal(Signal signal)
        {
            (Signal signal, string message) _result = (null, null);
            try
            {
                string url = this._serviceBaseUrl + DBOOM_RESTAPI.DELETE_SIGNAL;
                url.Replace("@signalId", signal.InternalId);
                HttpWebRequest webReq = _CreateWebRequest(HTTP_METHOD.DELETE, url);
                // log request
                _LogApiCall(webReq);
                HttpWebResponse webResponse = (HttpWebResponse)webReq.GetResponse();
                StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                string result = await reader.ReadToEndAsync();
                // log response
                _LogApiResponse(webReq, result);
                dynamic resultObj = JsonConvert.DeserializeObject(result);
                //JSON response
                string rId = resultObj["signal_token"];
                string rName = resultObj["description"];
                string rUnit = resultObj["unit_readable"];
                _result.signal = new Signal(signal.Guid, signal.InternalId, rId, rName, signal.Description, rUnit, signal.Readable, signal.Writable, signal.IsAlarm, signal.Rules);
                _result.message = null;
            }
            catch (Exception e)
            {
                Log.Error(string.Format("DBoomImpl.DeleteSignal >>> Error in deleting signal. error: {0} \n stack trace \n {1}", e.Message, e.StackTrace));
            }
            return _result;
        }

        public Task<Dictionary<string, Tuple<Device, string>>> GetDeviceLocations(List<Device> device)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<string, Tuple<Device, DEVICE_STATUS>>> GetDeviceStatus(List<Device> device)
        {
            throw new NotImplementedException();
        }

        public async Task<(Device device, string message)> InsertOrUpdateDevice(Device device)
        {
            (Device device, string message) _result = (null, null);
            try
            {
                string url = this._serviceBaseUrl;
                (List<Device> devices, string message) rDevices = await this.ReadDevices(device.InternalId);
                HttpWebRequest webReq;
                if (rDevices.devices.Count==0)
                {
                    url += DBOOM_RESTAPI.CREATE_DEVICE;
                    webReq = _CreateWebRequest(HTTP_METHOD.POST, url);
                }
                else
                {
                    url += DBOOM_RESTAPI.UPDATE_DEVICE;
                    url.Replace("@deviceId", device.InternalId);
                    webReq = _CreateWebRequest(HTTP_METHOD.PUT, url);
                }
                // log request
                _LogApiCall(webReq);

                // JSON payload
                ExpandoObject obj = new ExpandoObject();
                obj.TryAdd("description", device.Name);
                obj.TryAdd("device_token", device.Id);
                obj.TryAdd("type", "");
                obj.TryAdd("full_address", "");
                ExpandoObject geoCoordinatesObj = new ExpandoObject();
                geoCoordinatesObj.TryAdd("lat", device.GeoCoordinates.Split(";")[0]);
                geoCoordinatesObj.TryAdd("lng", device.GeoCoordinates.Split(";")[1]);
                obj.TryAdd("geoCoordinates", geoCoordinatesObj);
                ExpandoObject time_offsetObj = new ExpandoObject();
                time_offsetObj.TryAdd("device", "");
                time_offsetObj.TryAdd("view", "");
                obj.TryAdd("time_offset", time_offsetObj);
                ExpandoObject csv_optionsObj = new ExpandoObject();
                csv_optionsObj.TryAdd("date_format", "");
                csv_optionsObj.TryAdd("csv_signals", new string[] {""});
                csv_optionsObj.TryAdd("decimals_separator", "");
                obj.TryAdd("csv_options", csv_optionsObj);
                obj.TryAdd("notes", "");
                obj.TryAdd("tags", device.Tags);

                byte[] payload = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(obj, Formatting.None));
                Stream stream = webReq.GetRequestStream();
                stream.Write(payload, 0, payload.Length);

                HttpWebResponse webResponse = (HttpWebResponse)webReq.GetResponse();
                StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                string result = await reader.ReadToEndAsync();
                // log response
                _LogApiResponse(webReq, result);
                dynamic resultObj = JsonConvert.DeserializeObject(result);

                //JSON response
                string rId = resultObj["device_token"];
                string rName = resultObj["description"];
                string rCommStatus = resultObj["com_status"];
                if (rCommStatus.Equals("INIT") || rCommStatus.Equals("ACTIVE"))
                {
                    rCommStatus = Enum.GetName(typeof(DEVICE_STATUS), DEVICE_STATUS.ACTIVE);
                } 
                else
                {
                    rCommStatus = Enum.GetName(typeof(DEVICE_STATUS), DEVICE_STATUS.UNREACHABLE);
                }
                string rlastCommDateISOString = resultObj["lastCommDate"];
                List<Signal> rSignals = new List<Signal>();
                for (int i = 0; i < resultObj["signals"].Count; i++)
                {
                    string rSigIntId = resultObj["signals"][i];
                    rSignals.Add(new Signal("", rSigIntId, ""));
                }
                string rGeoCoordinates = resultObj["geoCoordinates"]["lat"] + ";" + resultObj["geoCoordinates"]["lng"];
                List<string> rTags = new List<string>();
                for (int i = 0; i < resultObj["tags"].Count; i++)
                {
                    string rTag = resultObj["tags"][i];
                    rTags.Add(rTag);
                }
                _result.device = new Device(device.Guid, device.InternalId, rId, rName, device.Description, rCommStatus, rlastCommDateISOString, rSignals, rGeoCoordinates, device.Parent, device.Children, rTags);
                _result.message = null;
            }
            catch (Exception e)
            {
                Log.Error(string.Format("DBoomImpl.InsertOrUpdateDevice >>> Error in creating or updating devices. error: {0} \n stack trace \n {1}", e.Message, e.StackTrace));
            }
            return _result;
        }

        public async Task<(Signal signal, string message)> InsertOrUpdateSignal(Signal signal)
        {
            (Signal signal, string message) _result = (null, null);
            try
            {
                string url = this._serviceBaseUrl;
                (List<Signal> signals, List<string> dvcIntIds,string message) rSignals = await this.ReadSignals(null, signal.InternalId);
                HttpWebRequest webReq;
                if (rSignals.signals.Count == 0)
                {
                    url += DBOOM_RESTAPI.CREATE_SIGNAL;
                    webReq = _CreateWebRequest(HTTP_METHOD.POST, url);
                }
                else
                {
                    url += DBOOM_RESTAPI.UPDATE_SIGNAL;
                    url.Replace("@signalId", signal.InternalId);
                    webReq = _CreateWebRequest(HTTP_METHOD.PUT, url);
                }
                // log request
                _LogApiCall(webReq);
                //JSON payload
                ExpandoObject obj = new ExpandoObject();
                obj.TryAdd("description", signal.Name);
                obj.TryAdd("signal_token", signal.Id);
                obj.TryAdd("type", "");
                obj.TryAdd("virtual_signal", false);
                obj.TryAdd("device", ""); 
                obj.TryAdd("notes", "");
                obj.TryAdd("tags", new string[] {""});

                byte[] payload = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(obj, Formatting.None));
                Stream stream = webReq.GetRequestStream();
                stream.Write(payload, 0, payload.Length);

                HttpWebResponse webResponse = (HttpWebResponse)webReq.GetResponse();
                StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                string result = await reader.ReadToEndAsync();
                // log response
                _LogApiResponse(webReq, result);
                dynamic resultObj = JsonConvert.DeserializeObject(result);

                //JSON response
                string rId = resultObj["signal_token"];
                string rName = resultObj["description"];
                string rUnit = resultObj["unit_readable"];
                _result.signal = new Signal(signal.Guid, signal.InternalId, rId, rName, signal.Description, rUnit, signal.Readable, signal.Writable, signal.IsAlarm, signal.Rules);
                _result.message = null;
            }
            catch (Exception e)
            {
                Log.Error(string.Format("DBoomImpl.InsertOrUpdateSignal >>> Error in creating or updating signals. error: {0} \n stack trace \n {1}", e.Message, e.StackTrace));
            }
            return _result;
        }

        public async Task<(List<Device> devices, string message)> ReadDevices(string internalId = null, string id = null, string nameLike = null, string descriptionLike = null, DEVICE_STATUS? commStatus = null, List<(string paramName, object value)> args = null)
        {

            (List<Device> devices, string message) _result = (null, null);
            try
            {
                string url = this._serviceBaseUrl;
                if (internalId == null && id == null)
                {
                    url += DBOOM_RESTAPI.GET_DEVICES_ALL;
                }
                else
                {
                    if (internalId != null)
                    {
                        url += DBOOM_RESTAPI.GET_DEVICE_BY_ID;
                        url = url.Replace("@deviceId", internalId);
                    }
                    else
                    {
                        url = this._serviceBaseUrl + DBOOM_RESTAPI.GET_DEVICE_BY_TOKEN;
                        url = url.Replace("@tokenId", id);
                    }
                }

                HttpWebRequest webReq = _CreateWebRequest(HTTP_METHOD.GET, url);
                // log request
                _LogApiCall(webReq);
                HttpWebResponse webResponse = (HttpWebResponse)webReq.GetResponse();
                StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                string result = await reader.ReadToEndAsync();
                // log response
                _LogApiResponse(webReq, result);
                dynamic resultObj = JsonConvert.DeserializeObject(result);
                List<Device> devices = new List<Device>();
                if (internalId == null && id == null)
                {
                    for (int i = 0; i < resultObj.Count; i++)
                    {
                        string rInternalId = resultObj[i]["_id"];
                        string rId = resultObj[i]["device_token"];
                        string rName = resultObj[i]["description"];
                        string rGeoCoordinates = resultObj[i]["geoCoordinates"]["lat"] + ";" + resultObj[i]["geoCoordinates"]["lng"];
                        List<Device> rChildren = new List<Device>();
                        for (int j = 0; j < resultObj[i]["children"].Count; j++)
                        {
                            string rChildIntId = resultObj[i]["children"][j];
                            rChildren.Add(new Device("", rChildIntId, ""));
                        }
                        List<string> rTags = new List<string>();
                        for (int j = 0; j < resultObj[i]["tags"].Count; j++)
                        {
                            string rTag = resultObj[i]["tags"][j];
                            rTags.Add(rTag);
                        }
                        devices.Add(new Device("", rInternalId, rId, rName, "", "", "2000-01-01 00:00:00.000", null, rGeoCoordinates, null, rChildren, rTags));
                    }
                }
                else
                {
                    string rInternalId = (internalId != null) ? resultObj["_id"] : "";
                    string rId = resultObj["device_token"];
                    string rName = resultObj["description"];
                    string rCommStatus = resultObj["com_status"];
                    if (rCommStatus.Equals("INIT") || rCommStatus.Equals("ACTIVE"))
                    {
                        rCommStatus = Enum.GetName(typeof(DEVICE_STATUS), DEVICE_STATUS.ACTIVE);
                    }
                    else
                    {
                        rCommStatus = Enum.GetName(typeof(DEVICE_STATUS), DEVICE_STATUS.UNREACHABLE);
                    }
                    string rlastCommDateISOString = resultObj["lastCommDate"];
                    List<Signal> rSignals = new List<Signal>();
                    for (int i = 0; i < resultObj["signals"].Count; i++)
                    {
                        string rSigIntId = resultObj["signals"][i];
                        rSignals.Add(new Signal("", rSigIntId, ""));
                    }
                    string rGeoCoordinates = resultObj["location"]["coordinates"][1] + ";" + resultObj["location"]["coordinates"][0];
                    List<Device> rChildren = new List<Device>();
                    for (int i = 0; i < resultObj["children"].Count; i++)
                    {
                        string rChildIntId = resultObj["children"][i];
                        rChildren.Add(new Device("", rChildIntId, ""));
                    }
                    List<string> rTags = new List<string>();
                    for (int i = 0; i < resultObj["tags"].Count; i++)
                    {
                        string rTag = resultObj["tags"][i];
                        rTags.Add(rTag);
                    }
                    devices.Add(new Device("", rInternalId, rId, rName, "", rCommStatus, rlastCommDateISOString, rSignals, rGeoCoordinates, null, rChildren, rTags));
                }
                //Applying filters to the output
                List<Device> delDevices = new List<Device>();
                for (int i = 0; i < devices.Count; i++)
                {
                    if ((nameLike != null && !devices[i].Name.Contains(nameLike))
                        || (commStatus != null && devices[i].CommStatus != null
                        && !(devices[i].CommStatus.Equals(Enum.GetName(typeof(DEVICE_STATUS), commStatus)))))
                    {
                        delDevices.Add(new Device("", devices[i].InternalId, ""));
                    }
                }
                for(int i=0; i<delDevices.Count; i++)
                {
                    for(int j = 0; j < devices.Count; j++)
                    {
                        if(devices[j].InternalId.Equals(delDevices[i].InternalId))
                        {
                            devices.RemoveAt(j);
                            j = devices.Count;
                        }
                    }
                }
                _result.devices = devices;
                _result.message = null;
            }
            catch (Exception e)
            {
                Log.Error(string.Format("DBoomImpl.ReadDevices >>> Error in getting devices. error: {0} \n stack trace \n {1}", e.Message, e.StackTrace));
            }
            return _result;
        }

        public async Task<(List<Signal> signals, List<string> dvcIntIds, string message)> ReadSignals(bool? isAlarm = null, string internalId = null, string id = null, string nameLike = null, string descriptionLike = null, string unit = null, bool? readable = null, bool? writable = null, List<(string paramName, object value)> args = null)
        {
            
            (List<Signal> signals, List<string> dvcIntIds, string message) _result = (null, null, null);
            try
            {
                List<Signal> signals = new List<Signal>();
                List<string> dvcIntIds = new List<string>();
                string url = this._serviceBaseUrl;
                if (internalId!=null)
                {
                    url += DBOOM_RESTAPI.GET_SIGNAL_BY_ID;
                    url = url.Replace("@signalId", internalId);
                }
                else
                {
                    url += DBOOM_RESTAPI.GET_SIGNALS_WITH_PAGINATION;
                    url = url.Replace("@pageNumber", "1");
                }
                HttpWebRequest webReq = _CreateWebRequest(HTTP_METHOD.GET, url);
                // log request
                _LogApiCall(webReq);
                HttpWebResponse webResponse = (HttpWebResponse)webReq.GetResponse();
                StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                string result = await reader.ReadToEndAsync();
                // log response
                _LogApiResponse(webReq, result);
                dynamic resultObj = JsonConvert.DeserializeObject(result);

                if(internalId != null)
                {
                    string rInternalId = resultObj["_id"];
                    string rId = resultObj["signal_token"];
                    string rName = resultObj["description"];
                    string rUnit = resultObj["unit_readable"];
                    bool rWritable = (resultObj["orders"].Count > 0);
                    bool rIsAlarm = false; 
                    if (resultObj["last_value_rules"].Count > 0)
                    {
                        rIsAlarm = true;
                    }
                    if(!rIsAlarm)
                    {
                        for (int i = 0; i < resultObj["tags"].Count && !rIsAlarm; i++)
                        {
                            string rTag = resultObj["tags"][i];
                            if(rTag.ToLower().Equals("alarm"))
                            {
                                rIsAlarm = true;
                            }
                        }
                    }
                    if(!rIsAlarm && rName.ToLower().Contains("alarm"))
                    {
                        rIsAlarm = true;
                    }    
                    string rDvcIntId = resultObj["device"]["_id"];
                    dvcIntIds.Add(rDvcIntId);
                    signals.Add(new Signal("", rInternalId, rId, rName, "", rUnit, true, rWritable, rIsAlarm, null));
                }
                else
                {
                    int rTotItems = resultObj["total_items"];
                    rTotItems = rTotItems / 100 + 1;
                    int i = 1;
                    while (i <= rTotItems) 
                    {
                        if(i>1)
                        {
                            url = this._serviceBaseUrl + DBOOM_RESTAPI.GET_SIGNALS_WITH_PAGINATION;
                            url = url.Replace("@pageNumber", i.ToString());
                            webReq = _CreateWebRequest(HTTP_METHOD.GET, url);
                            // log request
                            _LogApiCall(webReq);
                            webResponse = (HttpWebResponse)webReq.GetResponse();
                            reader = new StreamReader(webResponse.GetResponseStream());
                            result = await reader.ReadToEndAsync();
                            // log response
                            _LogApiResponse(webReq, result);
                            resultObj = JsonConvert.DeserializeObject(result);
                        }
                        for(int j = 0; j < resultObj["data"].Count; j++)
                        {
                            string rInternalId = resultObj["data"][j]["_id"];
                            string rId = resultObj["data"][j]["signal_token"];
                            string rName = resultObj["data"][j]["description"];
                            string rUnit = resultObj["data"][j]["unit_readable"];
                            bool rWritable = (resultObj["data"][j]["orders"].Count > 0);
                            bool rIsAlarm = false;
                            if (resultObj["data"][j]["last_value_rules"].Count > 0)
                            {
                                rIsAlarm = true;
                            }
                            if (!rIsAlarm)
                            {
                                for (int k = 0; k < resultObj["data"][j]["tags"].Count && !rIsAlarm; k++)
                                {
                                    string rTag = resultObj["data"][j]["tags"][k];
                                    if (rTag.ToLower().Equals("alarm"))
                                    {
                                        rIsAlarm = true;
                                    }
                                }
                            }
                            if (!rIsAlarm && rName.ToLower().Contains("alarm"))
                            {
                                rIsAlarm = true;
                            }
                            string rDvcIntId = resultObj["data"][j]["device"]["_id"];
                            dvcIntIds.Add(rDvcIntId);
                            signals.Add(new Signal("", rInternalId, rId, rName, "", rUnit, true, rWritable, rIsAlarm, null));
                        }
                        i++;
                    }

                    // Populating cached dictionary containing device-signal DBoom associations
                    for (int j = 0; j < dvcIntIds.Count; j++)
                    {
                        ConcurrentDictionary<string, Signal> relatedSignals = new ConcurrentDictionary<string, Signal>();
                        relatedSignals.TryAdd(signals[j].InternalId, signals[j]);
                        if (!this._DBoomDeviceToSignalRel.TryAdd(dvcIntIds[j], relatedSignals))
                        {
                            this._DBoomDeviceToSignalRel[dvcIntIds[j]].TryAdd(signals[j].InternalId, signals[j]);
                        }
                    }
                }

                List<Signal> delSignals = new List<Signal>();
                //Applying filters to the output
                for(int i=0; i<signals.Count; i++)
                {
                    if ((internalId != null && ((isAlarm != null && isAlarm != signals[i].IsAlarm) 
                        || (readable != null && readable != signals[i].Readable)
                        || (writable != null && writable != signals[i].Writable)))
                        || (id != null && !signals[i].Id.Equals(id))
                        || (nameLike != null && !signals[i].Name.Contains(nameLike))
                        || (unit != null && !signals[i].Unit.Equals(unit)))
                    {
                        delSignals.Add(new Signal("", signals[i].InternalId, ""));
                    }
                }
                for (int i = 0; i < delSignals.Count; i++)
                {
                    for (int j = 0; j < signals.Count; j++)
                    {
                        if (signals[j].InternalId.Equals(delSignals[i].InternalId))
                        {
                            signals.RemoveAt(j);
                            j = signals.Count;
                        }
                    }
                }
                _result.signals = signals;
                _result.dvcIntIds = dvcIntIds;
                _result.message = null;
            }
            catch (Exception e)
            {
                Log.Error(string.Format("DBoomImpl.ReadSignals >>> Error in getting signals. error: {0} \n stack trace \n {1}", e.Message, e.StackTrace));
            }
            return _result;
        }

        public Task<string> SendSignalToDevice(Signal signal, Device targetDevice)
        {
            throw new NotImplementedException();
        }

        #region UTILITY FUNCTIONS

        private HttpWebRequest _CreateWebRequest(HTTP_METHOD httpMethod, string url)
        {
            HttpWebRequest _webReq = (HttpWebRequest)WebRequest.Create(url);
            _webReq.Method = Enum.GetName(typeof(HTTP_METHOD), httpMethod);
            _webReq.ContentType = "application/json";
            _webReq.Headers.Add("Apikey", this._apikey);

            return _webReq;
        }
        #endregion
    }

    enum HTTP_METHOD
    {
        GET,
        POST,
        PUT,
        DELETE
    }
}
