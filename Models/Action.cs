using System;

namespace signals_hub.Models
{
	public class Action
	{
		public string Guid { get; set; }
		public string InternalId { get; set; }

		public Action(string guid, string internalId)
		{
			this.Guid = guid;
			this.InternalId = internalId;
		}
	}
}
