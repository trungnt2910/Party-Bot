using System.Collections.Generic;
using Victoria;

namespace PartyBot.DataStructs
{
    public class ServerData
    {
		public IEnumerable<LavaTrack> PendingSelect { get; set; } = null;

		public double Speed { get; set; } = 1;

		public LoopType LoopType { get; set; } = LoopType.None;
    }
}
