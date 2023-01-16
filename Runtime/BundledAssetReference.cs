using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BundleSystem
{
    [System.Serializable]
    public class BundledAssetReference<TObject> : System.IEquatable<BundledAssetReference<TObject>>, IBundledAssetReference 
        where TObject : UnityEngine.Object
    {
        [SerializeField]
        public long assetLocalId;

        [SerializeField]
        public bool isMainAsset;
        
        [SerializeField]
        public string assetGuid;

        public string GetAssetGuid()
        {
            return assetGuid;
        }

        public bool IsMainAsset()
        {
            return isMainAsset;
        }

        public long GetAssetLocalID()
        {
            return assetLocalId;
        }
        
        /// <summary>
        /// Load Asset
        /// </summary>
        public BundleRequest<TObject> LoadSync()
        {
            if (isMainAsset)
            {
                return new BundleRequest<TObject>(BundleManager.LoadByGuid<TObject>(assetGuid), true);
            }
            else
            {
                return new BundleRequest<TObject>(BundleManager.LoadByGuidAndLocalId<TObject>(assetGuid, assetLocalId), true);
            }
        }
        
        /// <summary>
        /// Load AssetAsync
        /// </summary>
        public BundleRequest<TObject> LoadAsync()
        {
            if (isMainAsset)
            {
                return BundleManager.LoadByGuidAsync<TObject>(assetGuid);
            }
            else
            {
                return BundleManager.LoadByGuidAndLocalIdAsync<TObject>(assetGuid, assetLocalId);
            }
        }

        /// <summary>
        /// Is specified asset exist in current bundle settings?
        /// </summary>
        public bool Exists()
        {
            if (isMainAsset)
            {
                return BundleManager.IsAssetExistByGuid(assetGuid);
            }
            else
            {
                return BundleManager.IsAssetExistByGuidAndLocalId(assetGuid, assetLocalId);
            }
        }

        public bool Equals(BundledAssetReference<TObject> other)
        {
            return assetGuid == other?.assetGuid && isMainAsset == other?.isMainAsset && assetLocalId == other?.assetLocalId;
        }
        
        public override int GetHashCode() 
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (assetGuid?.GetHashCode() ?? 0);
                hash = hash * 23 + assetLocalId.GetHashCode();
                hash = hash * 23 + isMainAsset.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            if(obj is BundledAssetReference<TObject> otherPath)
            {
                return Equals(otherPath);
            }
            return false;
        }

        public static bool operator ==(BundledAssetReference<TObject> lhs, BundledAssetReference<TObject> rhs) 
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(BundledAssetReference<TObject> lhs, BundledAssetReference<TObject> rhs) 
        {
            return !lhs.Equals(rhs);
        }
    }

    public interface IBundledAssetReference
    {
        string GetAssetGuid();
        bool IsMainAsset();
        long GetAssetLocalID();
    }

    public static class BundledAssetReferenceExtension
    {
        /// <summary>
        /// Load Asset
        /// </summary>
        public static BundleRequest<TObject> LoadSync<TObject>(this IBundledAssetReference reference) where TObject : Object
        {
            if (reference.IsMainAsset())
            {
                return new BundleRequest<TObject>(BundleManager.LoadByGuid<TObject>(reference.GetAssetGuid()), true);
            }
            else
            {
                return new BundleRequest<TObject>(BundleManager.LoadByGuidAndLocalId<TObject>(reference.GetAssetGuid(), reference.GetAssetLocalID()), true);
            }
        }
        
        /// <summary>
        /// Load AssetAsync
        /// </summary>
        public static BundleRequest<TObject> LoadAsync<TObject>(this IBundledAssetReference reference) where TObject : Object
        {
            if (reference.IsMainAsset())
            {
                return BundleManager.LoadByGuidAsync<TObject>(reference.GetAssetGuid());
            }
            else
            {
                return BundleManager.LoadByGuidAndLocalIdAsync<TObject>(reference.GetAssetGuid(), reference.GetAssetLocalID());
            }
        }

        /// <summary>
        /// Is specified asset exist in current bundle settings?
        /// </summary>
        public static bool Exists(this IBundledAssetReference reference)
        {
            if (reference.IsMainAsset())
            {
                return BundleManager.IsAssetExistByGuid(reference.GetAssetGuid());
            }
            else
            {
                return BundleManager.IsAssetExistByGuidAndLocalId(reference.GetAssetGuid(), reference.GetAssetLocalID());
            }
        }
    }
}

#if UNITY_EDITOR
namespace BundleSystem
{
    using UnityEditor;
    public abstract class BundledAssetReferenceDrawer<TObject> : PropertyDrawer where TObject : UnityEngine.Object
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 1;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            {
                var labelPosition = new Rect(position)
                {
                    width = EditorGUIUtility.labelWidth
                };
                EditorGUI.LabelField(labelPosition, label);

                var indentWidth = EditorGUI.indentLevel * 15;
                var referenceFieldPosition = new Rect(EditorGUI.IndentedRect(position))
                {
                    width = position.width - EditorGUIUtility.labelWidth + indentWidth - 2,
                    x = position.x + EditorGUIUtility.labelWidth - indentWidth + 2,
                };
                var assetGuidProperty = property.FindPropertyRelative(nameof(BundledAssetReference<TObject>.assetGuid));
                var assetLocalIdProperty = property.FindPropertyRelative(nameof(BundledAssetReference<TObject>.assetLocalId));
                var isMainAssetProperty = property.FindPropertyRelative(nameof(BundledAssetReference<TObject>.isMainAsset));
                var assetGuid = assetGuidProperty.stringValue;
                var assetLocalId = assetLocalIdProperty.longValue;
                var isMainAsset = isMainAssetProperty.boolValue;
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                var asset = 
                    isMainAsset 
                        ? mainAsset 
                        : subAssets.FirstOrDefault(asset =>
                        {
                            if (mainAsset != asset && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var eachGuid, out long eachLocalId))
                            {
                                return eachGuid == assetGuid && eachLocalId == assetLocalId;
                            }
                            else return false;
                        });
                
                var changedAsset = EditorGUI.ObjectField(referenceFieldPosition, asset, typeof(TObject), false) as TObject;
                if (changedAsset != asset)
                {
                    if (changedAsset == null)
                    {
                        assetGuidProperty.stringValue = null;
                        assetLocalIdProperty.longValue = 0;
                        isMainAssetProperty.boolValue = false;
                    }
                    else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(changedAsset, out var changedGuid, out long changedLocalId))
                    {
                        assetGuidProperty.stringValue = changedGuid;
                        assetLocalIdProperty.longValue = changedLocalId;
                        isMainAssetProperty.boolValue = changedAsset == mainAsset;
                    }
                    else
                    {
                        assetGuidProperty.stringValue = null;
                        assetLocalIdProperty.longValue = 0;
                        isMainAssetProperty.boolValue = false;
                    }
                }
            }
            EditorGUI.EndProperty();
        }
    }
}
#endif

namespace BundleSystem
{
    [Serializable]
    public sealed class BundledGameObjectReference : BundledAssetReference<GameObject>
    {
        #if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(BundledGameObjectReference))]
        public sealed class PropertyDrawer : BundledAssetReferenceDrawer<GameObject> {}
        #endif
    }
    
    [Serializable]
    public sealed class BundledAudioClipReference : BundledAssetReference<AudioClip>
    {
#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(BundledAudioClipReference))]
        public sealed class PropertyDrawer : BundledAssetReferenceDrawer<AudioClip> {}
#endif
    }
    
    [Serializable]
    public sealed class BundledSpriteReference : BundledAssetReference<Sprite>
    {
#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(BundledSpriteReference))]
        public sealed class PropertyDrawer : BundledAssetReferenceDrawer<Sprite> {}
#endif
    }
    
    [Serializable]
    public sealed class BundledTexture2DReference : BundledAssetReference<Texture2D>
    {
#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(BundledTexture2DReference))]
        public sealed class PropertyDrawer : BundledAssetReferenceDrawer<Texture2D> {}
#endif
    }
    
    [Serializable]
    public sealed class BundledTextAssetReference : BundledAssetReference<TextAsset>
    {
#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(BundledTextAssetReference))]
        public sealed class PropertyDrawer : BundledAssetReferenceDrawer<TextAsset> {}
#endif
    }
}
