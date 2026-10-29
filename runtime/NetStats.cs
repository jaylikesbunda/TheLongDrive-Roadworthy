using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TldMp
{
    // Read-only network cost counters for the [TLD MP net] diagnostics line.
    // Per window: native messages offered to the send queue by type, packets
    // actually handed to the transport, received messages by type, the deepest
    // per-peer send queue seen, coalesced frames, skipped collision restores,
    // remote-car pose arrival gaps and smoothing-buffer underruns.
    internal static class NetStats
    {
        static readonly long[] offeredCount = new long[256], offeredBytes = new long[256];
        static readonly long[] receivedCount = new long[256], receivedBytes = new long[256];
        static readonly long[] handlerTicks = new long[256], handlerMaxTicks = new long[256];
        // One received message slower than this is logged on its own line, and a
        // bridge Update slower than SlowUpdateMs is logged with its breakdown.
        internal const double SlowHandlerMs = 25.0;
        internal const double SlowUpdateMs = 50.0;
        static long sentCount, sentBytes, coalescedAtStart, maxPending, maxPendingBytes;
        // Position/velocity messages applied in order, and older ones dropped.
        internal static long SequencePassed, SequenceStale;
        static long poseSamples, poseGapOver100, poseGapOver250;
        static double poseGapSum, poseGapMax;
        static int coalesceState;

        // TLD_MP_NET_COALESCE=0 disables send-queue coalescing (A/B switch).
        internal static bool CoalesceEnabled
        {
            get
            {
                if (coalesceState == 0) coalesceState = Environment.GetEnvironmentVariable("TLD_MP_NET_COALESCE") == "0" ? 2 : 1;
                return coalesceState == 1;
            }
        }

        internal static void Offered(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            offeredCount[bytes[0]]++;
            offeredBytes[bytes[0]] += bytes.Length;
        }

        internal static void Sent(byte[] bytes)
        {
            if (bytes == null) return;
            sentCount++;
            sentBytes += bytes.Length;
        }

        internal static void Received(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            receivedCount[bytes[0]]++;
            receivedBytes[bytes[0]] += bytes.Length;
        }

        // Time spent in the native handler for one received message. True when
        // that single message was slow enough to log.
        internal static bool Handled(byte[] bytes, long ticks)
        {
            if (bytes == null || bytes.Length == 0 || ticks < 0) return false;
            handlerTicks[bytes[0]] += ticks;
            if (ticks > handlerMaxTicks[bytes[0]]) handlerMaxTicks[bytes[0]] = ticks;
            return ToMs(ticks) > SlowHandlerMs;
        }

        internal static bool IsSlowUpdate(double ms) { return ms > SlowUpdateMs; }

        internal static double ToMs(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }

        internal static string Ms(long ticks) { return ToMs(ticks).ToString("F2", CultureInfo.InvariantCulture); }

        // Slowest handler types as "type:maxMs/totalMs".
        internal static string TopHandlers(int limit)
        {
            var text = new StringBuilder();
            var used = new bool[256];
            for (int n = 0; n < limit; n++)
            {
                int best = -1;
                for (int i = 0; i < 256; i++) if (!used[i] && handlerTicks[i] > 0 && (best < 0 || handlerMaxTicks[i] > handlerMaxTicks[best])) best = i;
                if (best < 0) break;
                used[best] = true;
                if (text.Length > 0) text.Append(' ');
                text.Append(best).Append(':').Append(Ms(handlerMaxTicks[best])).Append('/').Append(Ms(handlerTicks[best]));
            }
            return text.ToString();
        }

        internal static void Pending(int entries, int bytes)
        {
            if (entries > maxPending) maxPending = entries;
            if (bytes > maxPendingBytes) maxPendingBytes = bytes;
        }

        // Seconds since the previous accepted pose of the same remote car.
        internal static void PoseGap(double seconds)
        {
            if (!(seconds > 0) || seconds > 5) return;
            poseSamples++;
            poseGapSum += seconds;
            if (seconds > poseGapMax) poseGapMax = seconds;
            if (seconds > 0.1) poseGapOver100++;
            if (seconds > 0.25) poseGapOver250++;
        }

        // Top message types by bytes, as "type:count/bytes".
        internal static string Top(long[] counts, long[] bytes, int limit)
        {
            var text = new StringBuilder();
            var used = new bool[256];
            for (int n = 0; n < limit; n++)
            {
                int best = -1;
                for (int i = 0; i < 256; i++) if (!used[i] && counts[i] > 0 && (best < 0 || bytes[i] > bytes[best])) best = i;
                if (best < 0) break;
                used[best] = true;
                if (text.Length > 0) text.Append(' ');
                text.Append(best).Append(':').Append(counts[best]).Append('/').Append(bytes[best]);
            }
            return text.ToString();
        }

        static string F(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }

        // One line per window; resets the window. seconds = window length.
        internal static string TakeLine(double now, double seconds, long coalescedTotal, CollisionRestoreCache cache, int interpolated, int extrapolated, int held)
        {
            double s = seconds > 0 ? seconds : 1;
            long offered = 0, offeredB = 0, received = 0, receivedB = 0;
            for (int i = 0; i < 256; i++) { offered += offeredCount[i]; offeredB += offeredBytes[i]; received += receivedCount[i]; receivedB += receivedBytes[i]; }
            var text = new StringBuilder("[TLD MP net] t=");
            text.Append(now.ToString("F3", CultureInfo.InvariantCulture)).Append(" window=").Append(s.ToString("F1", CultureInfo.InvariantCulture)).Append('s')
                .Append(" offeredUnreliable{msgs/s=").Append(F(offered / s)).Append(" bytes/s=").Append(F(offeredB / s)).Append(" top=").Append(Top(offeredCount, offeredBytes, 6)).Append('}')
                .Append(" sent{packets/s=").Append(F(sentCount / s)).Append(" bytes/s=").Append(F(sentBytes / s)).Append('}')
                .Append(" received{msgs/s=").Append(F(received / s)).Append(" bytes/s=").Append(F(receivedB / s)).Append(" top=").Append(Top(receivedCount, receivedBytes, 6)).Append('}')
                .Append(" handlerMs{top=").Append(TopHandlers(6)).Append('}')
                .Append(" sequence{applied=").Append(SequencePassed).Append(" staleDropped=").Append(SequenceStale).Append('}')
                .Append(" queue{maxPending=").Append(maxPending).Append(" maxPendingBytes=").Append(maxPendingBytes).Append(" coalesced=").Append(coalescedTotal - coalescedAtStart).Append('}');
            if (cache != null)
                text.Append(" collisionRestore{skipped=").Append(cache.Skipped).Append(" restored=").Append(cache.Restored)
                    .Append(" pairCallsAvoided=").Append(cache.PairsAvoided).Append(" invalidations=").Append(cache.Invalidations).Append('}');
            text.Append(" remoteCarPose{samples=").Append(poseSamples)
                .Append(" meanGapMs=").Append(poseSamples == 0 ? "0" : (poseGapSum / poseSamples * 1000).ToString("F1", CultureInfo.InvariantCulture))
                .Append(" maxGapMs=").Append((poseGapMax * 1000).ToString("F1", CultureInfo.InvariantCulture))
                .Append(" gapsOver100ms=").Append(poseGapOver100).Append(" gapsOver250ms=").Append(poseGapOver250)
                .Append(" interpolated=").Append(interpolated).Append(" extrapolated=").Append(extrapolated).Append(" held=").Append(held).Append('}');
            Array.Clear(offeredCount, 0, 256); Array.Clear(offeredBytes, 0, 256);
            Array.Clear(receivedCount, 0, 256); Array.Clear(receivedBytes, 0, 256);
            Array.Clear(handlerTicks, 0, 256); Array.Clear(handlerMaxTicks, 0, 256);
            sentCount = sentBytes = maxPending = maxPendingBytes = 0;
            SequencePassed = SequenceStale = 0;
            coalescedAtStart = coalescedTotal;
            poseSamples = poseGapOver100 = poseGapOver250 = 0;
            poseGapSum = poseGapMax = 0;
            if (cache != null) { cache.Skipped = cache.Restored = cache.PairsAvoided = cache.Invalidations = 0; }
            return text.ToString();
        }
    }
}
