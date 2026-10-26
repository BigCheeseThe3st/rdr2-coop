// Ride double (0.8.0): the rules both games agree on when a player sits behind a partner on
// their horse. The passenger is sent as on foot with RidingWith set, so their partner's game sits
// their stand-in behind the rider, and older versions show them beside the horse rather than on a
// second horse of their own. Pure .NET, for the tests and FakePlayer.
namespace RDR2Coop.Net
{
    public static class Riding
    {
        // What we send while we sit behind `rider`: where we are and how fast we go, no horse of our own.
        public static PlayerState AsPassenger(PlayerState s, byte rider)
        {
            s.Flags &= ~StateFlags.Mounted;
            s.MountModel = 0;
            s.MountPosition = new Vec3(0, 0, 0);
            s.MountHeading = 0;
            s.RidingWith = rider;
            return s;
        }

        // A player who can carry someone behind them: in the saddle, not down or dead.
        public static bool CanCarry(PlayerState rider) =>
            rider.Mounted && (rider.Flags & (StateFlags.Downed | StateFlags.Dead)) == 0;
    }
}
