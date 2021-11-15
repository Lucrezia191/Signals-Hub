using System;

namespace signals_hub.Models
{
	public class Rule
	{
		public string Guid { get; set; }
		public string InternalId { get; set; } //might not exist

		public Rule(string guid, string internalId)
		{
			this.Guid = guid;
			this.InternalId = internalId;
		}
	}
}
