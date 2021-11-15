using System;
using System.Collections.Generic;

namespace signals_hub.Models
{
	public class Workflow
	{
		public string Guid { get; set; }

		public string InternalId { get; set; }
		public List<Action> Actions { get; set; } 

		public Workflow(string guid, string internalId, List<Action> actions = null)
		{
			this.Guid = guid;
			this.InternalId = internalId;
			this.Actions = actions;
		}
	}
}
