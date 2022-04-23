﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Tau.Objects;

namespace osu.Game.Rulesets.Tau.Beatmaps
{
    public class TauBeatmapConverter : BeatmapConverter<TauHitObject>
    {
        // TODO: Create a more robust system.
        public override bool CanConvert() => true;

        protected override Beatmap<TauHitObject> CreateBeatmap() => new TauBeatmap();

        public bool CanConvertToHardBeats { get; set; } = true;
        public bool CanConvertToSliders { get; set; } = true;
        public bool CanConvertImpossibleSliders { get; set; } = false;
        public int SliderDivisor { get; set; } = 4;
        public RotationDirection? LockedDirection;

        public TauBeatmapConverter(Ruleset ruleset, IBeatmap beatmap)
            : base(beatmap, ruleset)
        {
        }

        protected override IEnumerable<TauHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            bool isHard = (original is IHasPathWithRepeats tmp ? tmp.NodeSamples[0] : original.Samples).Any(s => s.Name == HitSampleInfo.HIT_FINISH);

            return original switch
            {
                IHasPathWithRepeats path => convertToSlider(original, path, isHard, beatmap.Difficulty).Yield(),
                _ => isHard && CanConvertToHardBeats ? convertToHardBeat(original).Yield() : convertToBeat(original).Yield()
            };
        }

        private float? lastLockedAngle;
        float nextAngle ( float target ) {
            if ( lastLockedAngle is null || LockedDirection is null ) {
                lastLockedAngle = target;
                return lastLockedAngle.Value;
            }

            var diff = Extensions.GetDeltaAngle( target, lastLockedAngle.Value );
            if ( (diff > 0) == (LockedDirection == RotationDirection.Clockwise) ) {
                lastLockedAngle = target;
                return target;
            }
            else {
                lastLockedAngle = lastLockedAngle.Value - diff;
                return lastLockedAngle.Value;
            }
        }

        private TauHitObject convertToBeat(HitObject original)
        {
            float angle = original switch
            {
                IHasPosition pos => pos.Position.GetHitObjectAngle(),
                IHasXPosition xPos => xPos.X.Remap(0, 512, 0, 360),
                IHasYPosition yPos => yPos.Y.Remap(0, 384, 0, 360),
                IHasAngle ang => ang.Angle,
                _ => 0
            };

            return new Beat
            {
                Samples = original.Samples,
                StartTime = original.StartTime,
                Angle = nextAngle( angle )
            };
        }

        private TauHitObject convertToHardBeat(HitObject original) =>
            new HardBeat
            {
                Samples = original.Samples,
                StartTime = original.StartTime,
            };

        private TauHitObject convertToSlider(HitObject original, IHasPathWithRepeats data, bool isHard, IBeatmapDifficultyInfo info)
        {
            float? startLockedAngle = lastLockedAngle;
            TauHitObject convertBeat() {
                lastLockedAngle = startLockedAngle;
                return CanConvertToHardBeats && isHard ? convertToHardBeat( original ) : convertToBeat( original );
            }

            if (!CanConvertToSliders)
                return convertBeat();

            if (data.Duration < IBeatmapDifficultyInfo.DifficultyRange(info.ApproachRate, 1800, 1200, 450) / SliderDivisor)
                return convertBeat();

            var nodes = new List<SliderNode>();

            float? lastAngle = null;
            float? lastTime = null;
            float firstAngle = 0f;

            for (int t = 0; t < data.Duration; t += 20)
            {
                float angle = nextAngle((((IHasPosition)original).Position + data.CurvePositionAt(t / data.Duration)).GetHitObjectAngle());

                if (t == 0) 
                    firstAngle = angle;

                angle = Extensions.GetDeltaAngle(angle, firstAngle);

                // We don't want sliders that switch angles too fast. We would default to a normal note in this case
                if (!CanConvertImpossibleSliders)
                    if (lastAngle.HasValue && MathF.Abs(Extensions.GetDeltaAngle(lastAngle.Value, angle)) / MathF.Abs(lastTime.Value - t) > 0.6)
                        return convertBeat();

                lastAngle = angle;
                lastTime = t;
                nodes.Add(new SliderNode(t, angle));
            }

            var finalAngle = nextAngle((((IHasPosition)original).Position + data.CurvePositionAt(1)).GetHitObjectAngle());
            finalAngle = Extensions.GetDeltaAngle(finalAngle, firstAngle);

            if (!CanConvertImpossibleSliders)
                if (lastAngle.HasValue && MathF.Abs(Extensions.GetDeltaAngle(lastAngle.Value, finalAngle)) / Math.Abs(lastTime.Value - data.Duration) > 0.6)
                    return convertBeat();

            nodes.Add(new SliderNode((float)data.Duration, finalAngle));

            return new Slider
            {
                Samples = original.Samples,
                StartTime = original.StartTime,
                NodeSamples = data.NodeSamples,
                RepeatCount = data.RepeatCount,
                Angle = firstAngle,
                Path = new PolarSliderPath(nodes.ToArray())
            };
        }
    }
}
