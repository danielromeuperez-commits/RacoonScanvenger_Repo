using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Ajustes de importación de los sprites del HUD (Assets/_Racoon/2D/Sprites/UI/HUD): Sprite sin compresión (bordes
    /// nítidos) y con mipmaps nítidos (filtro Kaiser, trilineal y bias negativo) para que se vean bien también al reducirse
    /// en pantallas de 1080p.
    ///  - Paneles de las variantes: se exportan a 3840×2160 (densidad 1,5 respecto al lienzo de 2560×1440 del HUD), así
    ///    que van a 150 px por unidad y su tamaño nativo coincide con el del lienzo.
    ///  - _Comun: fichas en 9-slice para el texto P5 a 100 px por unidad. «9s_x.png» → borde de 8 px; «9s96_x.png» → 96 px.
    /// </summary>
    public class HUDSpriteImporter : AssetPostprocessor
    {
        public const string Root = "Assets/_Racoon/2D/Sprites/UI/HUD/";
        public const float PanelPixelsPerUnit = 150f;

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(Root))
                Configure((TextureImporter)assetImporter);
        }

        /// <summary>Aplica los ajustes; devuelve true si ha cambiado algo (hay que reimportar).</summary>
        public static bool Configure(TextureImporter importer)
        {
            var match = Regex.Match(Path.GetFileName(importer.assetPath), @"^9s(\d*)_");
            float b = match.Success ? (match.Groups[1].Value.Length > 0 ? float.Parse(match.Groups[1].Value) : 8f) : 0f;
            var border = new Vector4(b, b, b, b);
            float ppu = importer.assetPath.Contains("/_Comun/") ? 100f : PanelPixelsPerUnit;
            if (importer.textureType == TextureImporterType.Sprite && importer.mipmapEnabled &&
                importer.mipmapFilter == TextureImporterMipFilter.KaiserFilter && Mathf.Approximately(importer.mipMapBias, -.5f) &&
                importer.filterMode == FilterMode.Trilinear && importer.textureCompression == TextureImporterCompression.Uncompressed &&
                importer.spriteBorder == border && Mathf.Approximately(importer.spritePixelsPerUnit, ppu) && importer.maxTextureSize >= 4096)
                return false;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.mipMapBias = -.5f; // un poco más nítido al reducirse
            importer.filterMode = FilterMode.Trilinear;
            importer.alphaIsTransparency = true; // sin halos oscuros en los bordes de los mipmaps
            importer.spritePixelsPerUnit = ppu;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, 4096);
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
