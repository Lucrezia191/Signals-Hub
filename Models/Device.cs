using System;
using System.Collections.Generic;

namespace signals_hub.Models
{
    public class Device
    {
        public string Guid { get; set; } //device global unique identifier (like InternalId but created by us)
        public string InternalId { get; set; } //device uid (unique identifier inside the program)
        public string Id { get; set; } //device id  (unique identifier inside a table)
        public string Name { get; set; } //device name
        public string Description { get; set; } //device description
        public string CommStatus { get; set; } //communication status DBOOM("INIT" (=active),"ACTIVE" (=active),"COM_ERROR"(=unreachable)) ---> SIGNALS_HUB("ACTIVE","INACTIVE" (not used),"UNREACHABLE")
        public DateTime LastCommDate { get; set; } //last communication date
        public List<Signal> Signals { get; set; } //array of receivable and sendable signals
        public string GeoCoordinates { get; set; } //longitude, latitude
        public Device Parent { get; set; } //parent device
        public List<Device> Children { get; set; } //children devices
        public List<string> Tags { get; set; } //device tags (ex. device type)

        public Device(string guid, string internalId, string id, string name = null, string description = null, string commStatus = null, string lastCommDateISOString = null , List<Signal> signals = null, string geoCoordinates = null, Device parent = null, List<Device> children = null, List<string> tags = null)
        {
            this.Guid = guid;
            this.InternalId = internalId;
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.CommStatus = commStatus;
            if(lastCommDateISOString == null)
            {
                this.LastCommDate = new DateTime(2000, 01, 01, 0, 0, 0);
            } 
            else
            {
                this.LastCommDate = DateTime.Parse(lastCommDateISOString);
            }
            this.Signals = signals;
            this.GeoCoordinates = geoCoordinates;
            this.Parent = parent;
            this.Children = children;
            this.Tags = tags;
        }
    }
}
