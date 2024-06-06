using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using UnityEngine;

namespace KerbalCombatSystems.Data
{
    [AttributeUsage(AttributeTargets.Field)]
    public class Setting : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class Settings : Attribute
    {
        public string category = "Misc";
        public string displayName = "";
        public bool visible = true;
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class GlobalSettings : MonoBehaviour
    {
        private static string PluginData =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", Meta.name, "PluginData");

        private static string Config =>
            Path.Combine(PluginData, "settings.cfg");

        internal static int settingsVersion = 1;

        private struct CategoryInfo
        {
            public string name;
            public string displayName;
            public Dictionary<string, SettingInfo> settings;
        }

        private struct SettingInfo
        {
            public Setting attribute;
            public FieldInfo field;
            public object defaultValue;
        }

        private static readonly Dictionary<string, CategoryInfo> categories = new Dictionary<string, CategoryInfo>();
        private static bool locatedFields = false;

        protected void Start()
        {
            Load();
        }

        private static void Reflect()
        {
            locatedFields = true;
            categories.Clear();
            var assembly = Assembly.GetExecutingAssembly();
            Settings attribute;
            Setting setting;
            SettingInfo settingInfo;

            foreach (Type type in assembly.GetTypes())
            {
                attribute = (Settings)type.GetCustomAttribute(typeof(Settings), false);
                if (attribute != null)
                {
                    if (!categories.TryGetValue(attribute.category, out CategoryInfo categoryInfo))
                    {
                        categoryInfo = new CategoryInfo()
                        {
                            name = attribute.category,
                            displayName = attribute.displayName,
                            settings = new Dictionary<string, SettingInfo>()
                        };

                        categories.Add(attribute.category, categoryInfo);
                    }

                    if (categoryInfo.displayName == "")
                        categoryInfo.displayName = attribute.displayName;

                    foreach (FieldInfo field in type.GetFields())
                    {
                        setting = (Setting)field.GetCustomAttribute(typeof(Setting), false);
                        if (setting == null)
                            continue;

                        settingInfo = new SettingInfo()
                        {
                            attribute = setting,
                            field = field,
                            defaultValue = field.GetValue(null)
                        };

                        categoryInfo.settings.Add(field.Name, settingInfo);
                    }
                }
            }
        }

        internal static void Save()
        {
            if (!locatedFields)
                Reflect();

            if (!Directory.Exists(PluginData))
                Directory.CreateDirectory(PluginData);

            ConfigNode settingsNode = new ConfigNode(nameof(GlobalSettings));
            settingsNode.AddValue("version", settingsVersion);
            ConfigNode categoryNode;

            foreach (CategoryInfo category in categories.Values)
            {
                categoryNode = new ConfigNode(category.name);

                foreach (SettingInfo setting in category.settings.Values)
                    categoryNode.AddValue(setting.field.Name, setting.field.GetValue(null).ToString());

                settingsNode.AddNode(categoryNode);
            }

            ConfigNode file = new ConfigNode();
            file.AddNode(settingsNode);
            file.Save(Config);
        }

        internal void Load()
        {
            if (!File.Exists(Config))
                return;

            if (!locatedFields)
                Reflect();

            ConfigNode file = ConfigNode.Load(Config);
            ConfigNode settingsNode = file.GetNode(nameof(GlobalSettings));
            ConfigNode categoryNode;

            foreach (CategoryInfo category in categories.Values)
            {
                categoryNode = settingsNode.GetNode(category.name);
                if (categoryNode == null)
                    continue;

                foreach (SettingInfo setting in category.settings.Values)
                {
                    if (GetValue(categoryNode, setting.field, out object value))
                        setting.field.SetValue(null, value);
                }
            }
        }

        private bool GetValue(ConfigNode node, FieldInfo field, out object value)
        {
            Type type = field.FieldType;
            object[] parameters;
            MethodInfo method;
            bool success;
            value = null;

            if (type.IsEnum)
            {
                Enum output = null;
                success = node.TryGetEnum(field.Name, type, ref output);
                value = output;
            }
            else
            {
                // TryGetValue has a million overloads and I can't be bothered writing a huge switch statement.

                parameters = new object[] { field.Name, null };
                method = typeof(ConfigNode).GetMethod("TryGetValue", new Type[] { typeof(string), type.MakeByRefType() });
                if (method == null)
                    return false;

                success = (bool)method.Invoke(node, parameters);
                value = parameters[1];
            }

            return success;
        }

        public static void ResetSetting(string category, string setting)
        {
            if (!categories.TryGetValue(category, out CategoryInfo categoryInfo))
                return;

            if (!categoryInfo.settings.TryGetValue(setting, out SettingInfo settingInfo))
                return;

            if (settingInfo.defaultValue != null)
                settingInfo.field.SetValue(null, settingInfo.defaultValue);
        }

        public static void ResetAll()
        {
            foreach (var category in categories)
                foreach (var setting in category.Value.settings)
                    ResetSetting(category.Key, setting.Key);

            Save();
        }
    }
}
