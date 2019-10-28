using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Corecii.TrackMusic
{
    public class CustomDataInfo
    {
        public static string CustomDataPrefix = "CustomData:";

        public Type Type;
        public Type InnerType;
        public string SubPrefix = "";
        public string Prefix { get => CustomDataPrefix + SubPrefix; }
        public static Dictionary<Type, CustomDataInfo> Infos = new Dictionary<Type, CustomDataInfo>();
        public static Dictionary<Type, Dictionary<Type, CustomDataInfo>> DeepInfos = new Dictionary<Type, Dictionary<Type, CustomDataInfo>>();
        public CustomDataInfo(Type type, Type innerType, string prefix)
        {
            Type = type;
            InnerType = innerType;
            SubPrefix = prefix;
        }
        public void Register()
        {
            RegisterStatic(this);
        }
        public static void RegisterStatic(CustomDataInfo info)
        {
            Debug.Log($"Registering {info.Type} {info.InnerType} {info.SubPrefix}");
            if (!Infos.ContainsKey(info.Type))
            {
                Infos.Add(info.Type, info);
            }
            if (!DeepInfos.ContainsKey(info.InnerType))
            {
                DeepInfos.Add(info.InnerType, new Dictionary<Type, CustomDataInfo>());
            }
            var innerDict = DeepInfos[info.InnerType];
            if (!innerDict.ContainsKey(info.Type))
            {
                innerDict.Add(info.Type, info);
            }
        }
        public static CustomDataInfo GetInfo(Type type)
        {
            CustomDataInfo info = null;
            Infos.TryGetValue(type, out info);
            return info;
        }
        public static CustomDataInfo GetInfo(Type type, Type innerType)
        {
            Dictionary<Type, CustomDataInfo> dict = null;
            DeepInfos.TryGetValue(innerType, out dict);
            if (dict == null)
            {
                return null;
            }
            CustomDataInfo info = null;
            dict.TryGetValue(type, out info);
            return info;
        }

        public static string GetPrefix<T>()
        {
            return GetInfo(typeof(T)).Prefix;
        }
    }
    public abstract class CustomData<T>
    {

        public CustomDataInfo GetInfo()
        {
            return CustomDataInfo.GetInfo(GetType());
        }

        public string GetPrefix()
        {
            return GetInfo().Prefix;
        }

        public abstract string StringFromObject(T obj);
        public abstract void StringToObject(T obj, string str);

        public string DataStringFromObject(T obj)
        {
            var prefix = GetPrefix();
            var distanceStr = StringFromObject(obj);
            if (!distanceStr.StartsWith(prefix))
            {
                return null;
            }
            return distanceStr.Substring(prefix.Length);
        }
        public void DataStringToObject(T obj, string str)
        {
            var prefix = GetPrefix();
            StringToObject(obj, prefix + str);
        }

        public abstract bool ReadDataString(string data);
        public abstract string WriteDataString();

        public bool ReadObject(T obj)
        {
            var str = DataStringFromObject(obj);
            if (str == null)
            {
                return false;
            }
            return ReadDataString(str);
        }
        public void WriteObject(T obj)
        {
            var str = WriteDataString();
            DataStringToObject(obj, str);
        }
    }
}
