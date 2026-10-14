using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Racoon
{
    /// <summary>
    /// Dibuja los campos [SerializeReference, SubclassSelector] con un desplegable
    /// para elegir la subclase y, debajo, los campos de la clase elegida.
    /// </summary>
    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public class SubclassSelectorDrawer : PropertyDrawer
    {
        // Índice 0 = null ("Ninguno").
        static readonly Dictionary<Type, (Type[] types, GUIContent[] names)> optionsCache = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (property.propertyType != SerializedPropertyType.ManagedReference || !property.isExpanded)
                return height;

            foreach (SerializedProperty child in GetChildren(property))
                height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.LabelField(line, label.text, "Usa [SubclassSelector] junto a [SerializeReference]");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            // Cabecera: foldout con el nombre del campo + desplegable de tipos.
            Rect labelRect = new Rect(line.x, line.y, EditorGUIUtility.labelWidth, line.height);
            Rect popupRect = new Rect(labelRect.xMax + 2f, line.y, line.width - labelRect.width - 2f, line.height);

            bool hasChildren = GetChildren(property).Any();
            if (hasChildren)
                property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, label, true);
            else
                EditorGUI.LabelField(labelRect, label);

            var (types, names) = GetOptions(GetBaseType());
            int index = Array.IndexOf(types, property.managedReferenceValue?.GetType());

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(popupRect, index, names);
            bool typeChanged = EditorGUI.EndChangeCheck() && newIndex != index;
            EditorGUI.showMixedValue = false;
            EditorGUI.indentLevel = indent;

            if (typeChanged)
            {
                Type type = types[newIndex];
                property.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
                property.isExpanded = true;
                property.serializedObject.ApplyModifiedProperties();

                // Los hijos han cambiado: se dibujan en el siguiente repintado.
                EditorGUI.EndProperty();
                return;
            }

            // Campos propios del efecto elegido.
            if (hasChildren && property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
                foreach (SerializedProperty child in GetChildren(property))
                {
                    float height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), child, true);
                    y += height + EditorGUIUtility.standardVerticalSpacing;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        Type GetBaseType()
        {
            Type type = fieldInfo.FieldType;
            if (type.IsArray)
                return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return type.GetGenericArguments()[0];
            return type;
        }

        static (Type[] types, GUIContent[] names) GetOptions(Type baseType)
        {
            if (optionsCache.TryGetValue(baseType, out var options))
                return options;

            Type[] types = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract
                            && !t.IsGenericType
                            && t.IsSerializable
                            && !typeof(UnityEngine.Object).IsAssignableFrom(t)
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name)
                .Prepend(null)
                .ToArray();

            GUIContent[] names = types
                .Select(t => new GUIContent(t == null ? "Ninguno" : GetDisplayName(t)))
                .ToArray();

            options = (types, names);
            optionsCache[baseType] = options;
            return options;
        }

        // "BananaEffect" -> "Banana"
        static string GetDisplayName(Type type)
        {
            string name = type.Name;
            if (name.Length > "Effect".Length && name.EndsWith("Effect"))
                name = name.Substring(0, name.Length - "Effect".Length);
            return ObjectNames.NicifyVariableName(name);
        }

        static IEnumerable<SerializedProperty> GetChildren(SerializedProperty property)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = property.GetEndProperty();

            if (!iterator.NextVisible(true))
                yield break;

            do
            {
                if (SerializedProperty.EqualContents(iterator, end))
                    yield break;
                yield return iterator.Copy();
            }
            while (iterator.NextVisible(false));
        }
    }
}
