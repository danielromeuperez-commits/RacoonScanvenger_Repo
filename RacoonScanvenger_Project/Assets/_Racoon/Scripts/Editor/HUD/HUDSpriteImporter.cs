using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Ajustes de importación de los sprites del HUD (Assets/_Racoon/2D/Sprites/UI/HUD): Sprite, sin mipmaps,
    /// sin compresión (bordes nítidos), 100 px por unidad. Los archivos «9s_*» / «9s24_*» se importan como 9-slice.
    /// </summary>
    public class HUDSpriteImporter : AssetPostprocessor
    {
        public const string Root = "Assets/_Racoon/2D/Sprites/UI/HUD/";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(Root))
                Configure((TextureImporter)assetImporter);
        }

        /// <summary>Aplica los ajustes; devuelve true si ha cambiado algo (hay que reimportar).</summary>
        public static bool Configure(TextureImporter importer)
        {
            // «9s_x.png» → 9-slice con borde de 8 px; «9s24_x.png» → borde de 24 px
            var match = Regex.Match(Path.GetFileName(importer.assetPath), @"^9s(\d*)_");
            float b = match.Success ? (match.Groups[1].Value.Length > 0 ? float.Parse(match.Groups[1].Value) : 8f) : 0f;
            var border = new Vector4(b, b, b, b);
            if (importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled &&
                importer.textureCompression == TextureImporterCompression.Uncompressed &&
                importer.spriteBorder == border && Mathf.Approximately(importer.spritePixelsPerUnit, 100f))
                return false;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            return true;
        }
    }
}
