﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace nadena.dev.ndmf.animator
{
    [PublicAPI]
    public sealed class CloneContext
    {
        public IPlatformAnimatorBindings PlatformBindings { get; private set; }
        private readonly Dictionary<object, object> _clones = new();

        private int _cloneDepth, _nextVirtualLayer, _virtualLayerBase, _maxMappedPhysLayer;
        private readonly Queue<Action> _deferredCalls = new();

        private ImmutableList<AnimatorOverrideController> _overrideControllers =
            ImmutableList<AnimatorOverrideController>.Empty;

        public CloneContext(IPlatformAnimatorBindings platformBindings)
        {
            PlatformBindings = platformBindings;
            _nextVirtualLayer = _virtualLayerBase = 0x10_0000;
        }

        private class OverrideControllerScope : IDisposable
        {
            private readonly CloneContext _context;
            private readonly ImmutableList<AnimatorOverrideController> _priorStack;

            public OverrideControllerScope(CloneContext context, AnimatorOverrideController controller)
            {
                _context = context;
                _priorStack = context._overrideControllers;
                _context._overrideControllers = _priorStack.Add(controller);
            }

            public void Dispose()
            {
                _context._overrideControllers = _priorStack;
            }
        }

        internal IDisposable PushOverrideController(AnimatorOverrideController controller)
        {
            return new OverrideControllerScope(this, controller);
        }

        /// <summary>
        ///     Applies any in-scope AnimationOverrideControllers to the given motion to get the effective motion.
        /// </summary>
        /// <param name="clip"></param>
        /// <returns></returns>
        public AnimationClip MapClipOnClone(AnimationClip clip)
        {
            foreach (var controller in _overrideControllers)
            {
                clip = controller[clip];
            }

            return clip;
        }

        internal bool TryGetValue<T, U>(T key, out U? value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            
            var rv = _clones.TryGetValue(key, out var tmp);

            if (rv) value = (U?)tmp;
            else value = default;

            return rv;
        }

        private U? GetOrClone<T, U>(T? key, Func<CloneContext, T, U> clone) where U : class
        {
            try
            {
                _cloneDepth++;

                if (key == null || (key is Object obj && obj == null)) return null;
                if (TryGetValue(key, out U? value)) return value;
                value = clone(this, key);
                _clones[key] = value;
                return value;
            }
            finally
            {
                if (--_cloneDepth == 0)
                {
                    // Flush deferred actions. Note that deferred actions might spawn other clones, so be careful not
                    // to recurse while flushing.
                    try
                    {
                        _cloneDepth++;

                        while (_deferredCalls.TryDequeue(out var action)) action();
                    }
                    finally
                    {
                        _cloneDepth--;
                    }
                }
            }
        }

        internal int AllocateVirtualLayerSpace(int n)
        {
            var layerStart = _nextVirtualLayer;
            _nextVirtualLayer += n;
            _virtualLayerBase = layerStart;
            _maxMappedPhysLayer = n;

            return layerStart;
        }

        internal int AllocateSingleVirtualLayer()
        {
            return _nextVirtualLayer++;
        }

        [return: NotNullIfNotNull("controller")]
        public VirtualAnimatorController? Clone(RuntimeAnimatorController? controller)
        {
            using var _ = new ProfilerScope("Clone Animator Controller", controller);
            return GetOrClone(controller, VirtualAnimatorController.Clone);
        }

        [return: NotNullIfNotNull("layer")]
        public VirtualLayer? Clone(AnimatorControllerLayer? layer, int index)
        {
            using var _ = new ProfilerScope("Clone Animator Layer");
            return GetOrClone(layer, (ctx, obj) => VirtualLayer.Clone(ctx, obj, index));
        }

        [return: NotNullIfNotNull("stateMachine")]
        public VirtualStateMachine? Clone(AnimatorStateMachine? stateMachine)
        {
            using var _ = new ProfilerScope("Clone State Machine", stateMachine);
            return GetOrClone(stateMachine, VirtualStateMachine.Clone);
        }

        [return: NotNullIfNotNull("transition")]
        public VirtualStateTransition? Clone(AnimatorStateTransition? transition)
        {
            using var _ = new ProfilerScope("Clone State Transition", transition);
            return GetOrClone(transition, VirtualStateTransition.Clone);
        }

        [return: NotNullIfNotNull("transition")]
        public VirtualTransition? Clone(AnimatorTransition? transition)
        {
            using var _ = new ProfilerScope("Clone Transition", transition);
            return GetOrClone(transition, VirtualTransition.Clone);
        }

        [return: NotNullIfNotNull("state")]
        public VirtualState? Clone(AnimatorState? state)
        {
            using var _ = new ProfilerScope("Clone State", state);
            return GetOrClone(state, VirtualState.Clone);
        }

        [return: NotNullIfNotNull("m")]
        public VirtualMotion? Clone(Motion? m)
        {
            using var _ = new ProfilerScope("Clone Motion", m);
            return GetOrClone(m, VirtualMotion.Clone);
        }

        [return: NotNullIfNotNull("clip")]
        public VirtualClip? Clone(AnimationClip? clip)
        {
            using var _ = new ProfilerScope("Clone Clip", clip);
            return GetOrClone(clip, VirtualClip.Clone);
        }

        public void DeferCall(Action action)
        {
            var overrideStack = _overrideControllers;
            // Preserve ambient AnimatorOverrideController context when we defer calls
            if (_cloneDepth > 0)
                _deferredCalls.Enqueue(() =>
                {
                    var priorStack = _overrideControllers;
                    _overrideControllers = overrideStack;
                    try
                    {
                        action();
                    }
                    finally
                    {
                        _overrideControllers = priorStack;
                    }
                });
            else action();
        }

        public int CloneSourceToVirtualLayerIndex(int layerSyncedLayerIndex)
        {
            return layerSyncedLayerIndex < _maxMappedPhysLayer && layerSyncedLayerIndex >= 0
                ? layerSyncedLayerIndex + _virtualLayerBase
                : -1;
        }
    }
}