using System;
using System.Collections.Generic;

namespace signals_hub.Models
{
    public class Signal
    {
        public string Guid { get; set; }
        public string InternalId { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public bool IsAlarm { get; set; }
        public bool Readable { get; set; }
        public bool Writable { get; set; }
        public List<Rule> Rules { get; set; }

        //// limited constructor for inheritance
        //protected Signal()
        //{

        //}

        public Signal(string guid, string internalId, string id, string name = null, string description = null, string unit = null, bool readable = true, bool writable = false, bool isAlarm = false, List<Rule> rules = null)
        {
            this.Guid = guid;
            this.InternalId = internalId;
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.Unit = unit;
            this.Readable = readable;
            this.Writable = writable;
            this.IsAlarm = isAlarm;
            this.Rules = rules;
        }

       
    }
}
