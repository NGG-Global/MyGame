using System;
using System.IO;
using System.Reflection;
using OpenSkies.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Creates the game's Audio Mixer and audio configuration from code, so they are
    /// reviewable and never depend on Editor clicks. Both are created once and then
    /// left alone, like the other configuration assets (tuning in the Editor survives
    /// scene rebuilds).
    ///
    /// Mixer layout: Master → Aircraft (Lowpass Simple), Environment (Lowpass Simple),
    /// Interface, Warnings. Exposed: MasterVolume, AircraftVolume, EnvironmentVolume,
    /// InterfaceVolume, WarningsVolume (dB) and AircraftCutoff, EnvironmentCutoff (Hz).
    ///
    /// Unity has no public API to build a mixer, so this uses the Editor's own mixer
    /// controller (the code behind Create › Audio Mixer and the mixer window) by
    /// reflection. The member names were checked against the Unity 6000.6.0f1 Editor
    /// assemblies. If a future Editor changes them, the builder logs a warning and the
    /// game still plays: <see cref="AudioManager"/> applies volumes and the cockpit
    /// filter on its sources when the mixer or a parameter is missing.
    /// </summary>
    public static class AudioMixerSetup
    {
        public const string MixerPath = "Assets/_Game/Audio/OpenSkiesMixer.mixer";
        public const string ConfigurationPath = "Assets/_Game/Settings/Audio/GameAudioConfiguration.asset";
        const string LowpassEffect = "Lowpass Simple";
        const string CutoffParameter = "Cutoff freq";
        const float OpenCutoff = 22000f;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The audio configuration with the mixer assigned (created once, never overwritten).</summary>
        public static GameAudioConfiguration LoadOrCreateConfiguration()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<GameAudioConfiguration>(ConfigurationPath);
            if (configuration != null)
            {
                // A configuration made before the mixer existed gets it, nothing else changes.
                if (configuration.mixer == null)
                {
                    configuration.mixer = LoadOrCreateMixer(configuration.parameters);
                    if (configuration.mixer != null) EditorUtility.SetDirty(configuration);
                }
                return configuration;
            }

            EditorFolders.Ensure(Path.GetDirectoryName(ConfigurationPath));
            configuration = ScriptableObject.CreateInstance<GameAudioConfiguration>();
            configuration.mixer = LoadOrCreateMixer(configuration.parameters);
            AssetDatabase.CreateAsset(configuration, ConfigurationPath);
            Debug.Log($"[OpenSkies] Created {ConfigurationPath}");
            return configuration;
        }

        public static AudioMixer LoadOrCreateMixer(MixerParameterNames names)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (existing != null) return existing;
            try
            {
                EditorFolders.Ensure(Path.GetDirectoryName(MixerPath));
                Build(names);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(MixerPath);
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
                if (mixer != null) Debug.Log($"[OpenSkies] Created {MixerPath}");
                return mixer;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OpenSkies] The audio mixer could not be generated ({e.GetBaseException().Message}). " +
                                 "Audio still works: volumes and the cockpit filter are applied on the sources. " +
                                 "Create the mixer by hand (see docs/MOBILE_PERFORMANCE_REPORT.md, Audio) or update AudioMixerSetup.");
                return AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            }
        }

        static void Build(MixerParameterNames names)
        {
            var controllerType = EditorType("UnityEditor.Audio.AudioMixerController");
            var groupType = EditorType("UnityEditor.Audio.AudioMixerGroupController");
            var effectType = EditorType("UnityEditor.Audio.AudioMixerEffectController");
            var groupPathType = EditorType("UnityEditor.Audio.AudioGroupParameterPath");
            var effectPathType = EditorType("UnityEditor.Audio.AudioEffectParameterPath");

            object controller = Method(controllerType, "CreateMixerControllerAtPath", Static).Invoke(null, new object[] { MixerPath });
            if (controller == null) throw new InvalidOperationException("CreateMixerControllerAtPath returned nothing");
            object master = Property(controllerType, "masterGroup").GetValue(controller);
            object snapshot = Property(controllerType, "TargetSnapshot").GetValue(controller);

            Expose(controller, controllerType, groupType, groupPathType, master, names.masterVolume);
            object aircraft = AddGroup(controller, controllerType, master, names.aircraftGroup);
            object environment = AddGroup(controller, controllerType, master, names.environmentGroup);
            object ui = AddGroup(controller, controllerType, master, names.interfaceGroup);
            object warnings = AddGroup(controller, controllerType, master, names.warningsGroup);
            Expose(controller, controllerType, groupType, groupPathType, aircraft, names.aircraftVolume);
            Expose(controller, controllerType, groupType, groupPathType, environment, names.environmentVolume);
            Expose(controller, controllerType, groupType, groupPathType, ui, names.interfaceVolume);
            Expose(controller, controllerType, groupType, groupPathType, warnings, names.warningsVolume);
            AddLowpass(controller, controllerType, groupType, effectType, effectPathType, aircraft, snapshot, names.aircraftCutoff);
            AddLowpass(controller, controllerType, groupType, effectType, effectPathType, environment, snapshot, names.environmentCutoff);

            EditorUtility.SetDirty((Object)controller);
        }

        static object AddGroup(object controller, Type controllerType, object parent, string name)
        {
            object group = Method(controllerType, "CreateNewGroup", Instance).Invoke(controller, new object[] { name, false });
            Method(controllerType, "AddChildToParent", Instance).Invoke(controller, new[] { group, parent });
            Method(controllerType, "AddGroupToCurrentView", Instance).Invoke(controller, new[] { group });
            return group;
        }

        static void Expose(object controller, Type controllerType, Type groupType, Type groupPathType, object group, string name)
        {
            var guid = (GUID)Method(groupType, "GetGUIDForVolume", Instance).Invoke(group, null);
            object path = Activator.CreateInstance(groupPathType, group, guid);
            Method(controllerType, "AddExposedParameter", Instance).Invoke(controller, new[] { path });
            Rename(controller, controllerType, guid, name);
        }

        static void AddLowpass(object controller, Type controllerType, Type groupType, Type effectType, Type effectPathType, object group,
            object snapshot, string name)
        {
            object effect = Activator.CreateInstance(effectType, LowpassEffect);
            Method(effectType, "PreallocateGUIDs", Instance).Invoke(effect, null);
            var effects = (Array)Property(groupType, "effects").GetValue(group);
            Method(groupType, "InsertEffect", Instance).Invoke(group, new[] { effect, (object)effects.Length }); // after the fader
            AssetDatabase.AddObjectToAsset((Object)effect, (Object)controller);
            if (snapshot != null)
                Method(effectType, "SetValueForParameter", Instance).Invoke(effect, new[] { controller, snapshot, CutoffParameter, (object)OpenCutoff });

            var guid = (GUID)Method(effectType, "GetGUIDForParameter", Instance).Invoke(effect, new object[] { CutoffParameter });
            object path = Activator.CreateInstance(effectPathType, group, effect, guid);
            Method(controllerType, "AddExposedParameter", Instance).Invoke(controller, new[] { path });
            Rename(controller, controllerType, guid, name);
        }

        /// <summary>Exposed parameters get generated names ("MyExposedParam"); give them the ones the game uses.</summary>
        static void Rename(object controller, Type controllerType, GUID guid, string name)
        {
            var property = Property(controllerType, "exposedParameters");
            var parameters = (Array)property.GetValue(controller);
            for (int i = 0; i < parameters.Length; i++)
            {
                object parameter = parameters.GetValue(i);
                var type = parameter.GetType();
                var guidField = type.GetField("guid", Instance);
                var nameField = type.GetField("name", Instance);
                if (guidField == null || nameField == null) throw new MissingFieldException(type.FullName, "guid/name");
                if (!guid.Equals(guidField.GetValue(parameter))) continue;
                nameField.SetValue(parameter, name); // boxed copy for a struct: written back below
                parameters.SetValue(parameter, i);
                property.SetValue(controller, parameters);
                return;
            }
            throw new InvalidOperationException("exposed parameter not found for " + name);
        }

        static Type EditorType(string fullName)
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType(fullName);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            throw new TypeLoadException(fullName);
        }

        static MethodInfo Method(Type type, string name, BindingFlags flags) =>
            type.GetMethod(name, flags) ?? throw new MissingMethodException(type.FullName, name);

        static PropertyInfo Property(Type type, string name) =>
            type.GetProperty(name, Instance) ?? throw new MissingMemberException(type.FullName, name);
    }
}
