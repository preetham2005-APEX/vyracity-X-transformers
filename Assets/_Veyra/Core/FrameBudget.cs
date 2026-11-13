using System.Collections;
using System.Diagnostics;

namespace VeyraCity.Core
{
    /// <summary>
    /// Frame-budgeted work pump (BUG-170). The world scatter systems used to build their whole slot table in a single
    /// frame — <c>StreetTrees</c> alone cost 225 ms on that frame and triggered the 2,037 ms worst frame of iteration 41,
    /// because each candidate position fires road queries and a 200 m raycast. The work is unavoidable, but doing it all
    /// at once is not: a system turns its build into an <see cref="IEnumerator"/> that yields often, and
    /// <see cref="Pump"/> advances it only until the frame's millisecond budget is spent.
    ///
    /// Yield granularity decides the overshoot: the pump can only stop at a yield, so a step that is itself expensive
    /// still blows the budget. Yield at least every few candidate positions, not once per road.
    /// </summary>
    public static class FrameBudget
    {
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>Default slice for background world building: invisible at 60 FPS (16.6 ms/frame).</summary>
        public const float DefaultMs = 2f;

        /// <summary>
        /// Advances <paramref name="job"/> until it finishes or <paramref name="budgetMs"/> is spent.
        /// Returns true while the job still has work, false once it is done (then drop the reference).
        /// </summary>
        public static bool Pump(IEnumerator job, float budgetMs = DefaultMs)
        {
            if (job == null) return false;
            long t0 = Stopwatch.GetTimestamp();
            do
            {
                if (!job.MoveNext()) return false;
            }
            while ((Stopwatch.GetTimestamp() - t0) * TickMs < budgetMs);
            return true;
        }
    }
}
