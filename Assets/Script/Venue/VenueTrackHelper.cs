using YARG.Core;
using YARG.Core.Chart;

namespace YARG.Venue
{
    public static class VenueTrackHelper
    {
        public static InstrumentDifficulty<T> GetAnimationDifficulty<T>(this InstrumentTrack<T> track)
            where T : Note<T>
        {
            if (track.TryGetDifficulty(Difficulty.Expert, out var expert) && expert.Notes.Count > 0)
            {
                return expert;
            }

            // Charts without Expert notes should still animate from their best available part.
            for (var difficulty = Difficulty.ExpertPlus; ; difficulty--)
            {
                if (track.TryGetDifficulty(difficulty, out var candidate) && candidate.Notes.Count > 0)
                {
                    return candidate;
                }
                if (difficulty == Difficulty.Beginner)
                {
                    break;
                }
            }

            // Missing instruments stay idle rather than borrowing another instrument's notes.
            return expert ?? new InstrumentDifficulty<T>(track.Instrument, Difficulty.Expert);
        }
    }
}
