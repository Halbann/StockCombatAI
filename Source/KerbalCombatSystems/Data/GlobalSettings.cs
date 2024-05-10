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
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", gameDataName, "PluginData");

        private static string Config =>
            Path.Combine(PluginData, "settings.cfg");

        internal static string gameDataName = "KCS";
        internal static int settingsVersion = 1;

        private struct CategoryInfo
        {
            public string name;
            public string displayName;
            public List<FieldInfo> fields;
        }

        private static Dictionary<string, CategoryInfo> categories = new Dictionary<string, CategoryInfo>();
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
            CategoryInfo categoryInfo;

            foreach (Type type in assembly.GetTypes())
            {
                attribute = (Settings)type.GetCustomAttribute(typeof(Settings), false);
                if (attribute != null)
                {
                    if (!categories.TryGetValue(attribute.category, out categoryInfo))
                    {
                        categoryInfo = new CategoryInfo()
                        {
                            name = attribute.category,
                            displayName = attribute.displayName,
                            fields = new List<FieldInfo>()
                        };

                        categories.Add(attribute.category, categoryInfo);
                    }

                    if (categoryInfo.displayName == "")
                        categoryInfo.displayName = attribute.displayName;

                    foreach (FieldInfo field in type.GetFields())
                    {
                        if (field.GetCustomAttribute(typeof(Setting), false) == null)
                            continue;

                        categoryInfo.fields.Add(field);
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
            CategoryInfo category;

            foreach (var entry in categories)
            {
                category = entry.Value;
                categoryNode = new ConfigNode(category.name);

                foreach (FieldInfo field in category.fields)
                {
                    categoryNode.AddValue(field.Name, field.GetValue(null).ToString());
                }

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
            CategoryInfo category;

            foreach (var entry in categories)
            {
                category = entry.Value;
                categoryNode = settingsNode.GetNode(category.name);
                if (categoryNode == null)
                    continue;

                foreach (FieldInfo field in category.fields)
                {
                    if (GetValue(categoryNode, field, out object value))
                        field.SetValue(null, value);
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
    }
}
