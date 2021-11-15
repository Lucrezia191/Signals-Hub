using System;

namespace signals_hub.Models
{
	public class Stage
	{
		public string Guid { get; set; }
		public string InternalId { get; set; }

		public Stage(string guid, string internalId)
		{
			this.Guid = guid;
			this.InternalId = internalId;
		}
	}
}
