﻿#region

using System;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.cs;
using UnityEngine;
#if NDMF_VRCSDK3_AVATARS
using VRC.SDK3.Avatars.Components;

#else
using nadena.dev.ndmf.runtime;
#endif

#endregion

namespace nadena.dev.ndmf.preview
{
    public static partial class ComputeContextQueries
    {
        /// <summary>
        /// Returns a list of all root game objects in all loaded scenes. Excludes objects with
        /// nonzero hide flags.
        /// </summary>
        public static ImmutableList<GameObject> GetSceneRoots(this ComputeContext ctx)
        {
            return ObjectWatcher.Instance.MonitorSceneRoots(ctx);
        }

        /// <summary>
        /// Returns a reactive value that evaluates to a list of all components of the given type in the scene.
        /// Excludes components found on a hidden game object, or under a hidden scene root (where hidden means
        /// hideFlags are nonzero)
        /// </summary>
        /// <typeparam name="T">The type to search for</typeparam>
        /// <returns></returns>
        public static ImmutableList<T> GetComponentsByType<T>(this ComputeContext ctx) where T : class
        {
            var roots = ctx.GetSceneRoots();

            var components =
                roots.SelectMany(root => ctx.GetComponentsInChildren<T>(root, true));

            return components.ToImmutableList();
        }

        public static ImmutableList<Component> GetComponentsByType(this ComputeContext ctx, Type type)
        {
            var roots = ctx.GetSceneRoots();

            var components =
                roots.SelectMany(root => ctx.GetComponentsInChildren(root, type, true));

            return components.ToImmutableList();
        }

        public static ImmutableList<GameObject> GetAvatarRoots(this ComputeContext ctx)
        {
            // TODO: multiple platform support
#if NDMF_VRCSDK3_AVATARS
            return ctx.GetComponentsByType<VRCAvatarDescriptor>()
                .Select(c => c.gameObject).ToImmutableList();
#else
            return ctx.GetComponentsByType<Animator>()
                .Select(c => c.gameObject)
                .Where(g => RuntimeUtil.IsAvatarRoot(g.transform))
                .ToImmutableList();
#endif
        }
    }
}