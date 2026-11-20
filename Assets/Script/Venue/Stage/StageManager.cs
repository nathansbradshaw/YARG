using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Gameplay;
using YARG.Core.Logging;

namespace YARG.Venue.Stage
{
    public class StageManager : GameplayBehaviour
    {
        [SerializeField]
        private GameObject _venue;

        private StageElement[] _stageElements;
        private bool           _hasStageEvents;

        private List<StageEffectEvent> _stageEvents;
        private int                    _stageEventIndex = 0;

        private List<StageElement> _pyroElements = new();
        private List<StageElement> _fogElements = new();

        protected override void OnChartLoaded(SongChart chart)
        {
            // Older venue bundles may not have serialized the stage root.
            if (_venue == null)
            {
                var manager = GetComponentInParent<BundleBackgroundManager>(true);
                if (manager == null)
                {
                    YargLogger.LogWarning("Stage manager has no venue root; stage effects are unavailable.");
                    return;
                }

                _venue = manager.gameObject;
            }

            _stageElements = _venue.GetComponentsInChildren<StageElement>();

            if (_stageElements.Length == 0)
            {
                return;
            }

            foreach (var stageElement in _stageElements)
            {
                if (stageElement.ElementType == StageElementType.Pyro)
                {
                    _pyroElements.Add(stageElement);
                }
                else if (stageElement.ElementType == StageElementType.Fog)
                {
                    _fogElements.Add(stageElement);
                }
            }

            _stageEvents = chart.VenueTrack.Stage;
            if (_stageEvents.Count > 0)
            {
                _hasStageEvents = true;
            }
        }

        private void Update()
        {
            if (!_hasStageEvents)
            {
                return;
            }

            // Check for stage events and dispatch them as required
            while (_stageEventIndex < _stageEvents.Count &&
                _stageEvents[_stageEventIndex].Time <= GameManager.VisualTime)
            {
                var stageEvent = _stageEvents[_stageEventIndex];
                _stageEventIndex++;

                // TODO: Handle optional once the fail meter exists
                switch (stageEvent.Effect)
                {
                    case StageEffect.BonusFx:
                        TriggerBonusFx(stageEvent);
                        break;
                    case StageEffect.FogOn:
                        StartFog(stageEvent);
                        break;
                    case StageEffect.FogOff:
                        StopFog(stageEvent);
                        break;
                }
            }
        }

        private void TriggerBonusFx(StageEffectEvent stageEvent)
        {
            foreach (var stageElement in _pyroElements)
            {
                stageElement.StartEffect();
            }
        }

        private void StartFog(StageEffectEvent stageEvent)
        {
            foreach (var stageElement in _fogElements)
            {
                stageElement.StartEffect();
            }
        }

        private void StopFog(StageEffectEvent stageEvent)
        {
            foreach (var stageElement in _fogElements)
            {
                stageElement.StopEffect();
            }
        }
    }


}
