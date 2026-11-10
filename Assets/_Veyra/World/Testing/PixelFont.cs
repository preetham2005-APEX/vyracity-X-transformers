using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.Testing
{
    /// <summary>
    /// A 5 x 7 bitmap font for labelling contact sheets without fonts, materials or shaders (works in the Editor and in
    /// player builds). Upper-case letters, digits and _ - . : ( ) / , + ; lower case is drawn upper case, anything else as '?'.
    /// </summary>
    public static class PixelFont
    {
        const int W = 5, H = 7;
        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['A'] = "01110100011000111111100011000110001", ['B'] = "11110100011000111110100011000111110",
            ['C'] = "01110100011000010000100001000101110", ['D'] = "11110100011000110001100011000111110",
            ['E'] = "11111100001000011110100001000011111", ['F'] = "11111100001000011110100001000010000",
            ['G'] = "01110100011000010111100011000101111", ['H'] = "10001100011000111111100011000110001",
            ['I'] = "01110001000010000100001000010001110", ['J'] = "00111000100001000010000101001001100",
            ['K'] = "10001100101010011000101001001010001", ['L'] = "10000100001000010000100001000011111",
            ['M'] = "10001110111010110101100011000110001", ['N'] = "10001100011100110101100111000110001",
            ['O'] = "01110100011000110001100011000101110", ['P'] = "11110100011000111110100001000010000",
            ['Q'] = "01110100011000110001101011001001101", ['R'] = "11110100011000111110101001001010001",
            ['S'] = "01111100001000001110000010000111110", ['T'] = "11111001000010000100001000010000100",
            ['U'] = "10001100011000110001100011000101110", ['V'] = "10001100011000110001100010101000100",
            ['W'] = "10001100011000110101101011010101010", ['X'] = "10001100010101000100010101000110001",
            ['Y'] = "10001100011000101010001000010000100", ['Z'] = "11111000010001000100010001000011111",
            ['0'] = "01110100011001110101110011000101110", ['1'] = "00100011000010000100001000010001110",
            ['2'] = "01110100010000100010001000100011111", ['3'] = "11111000100010000010000011000101110",
            ['4'] = "00010001100101010010111110001000010", ['5'] = "11111100001111000001000011000101110",
            ['6'] = "00110010001000011110100011000101110", ['7'] = "11111000010001000100010000100001000",
            ['8'] = "01110100011000101110100011000101110", ['9'] = "01110100011000101111000010001001100",
            ['_'] = "00000000000000000000000000000011111", ['-'] = "00000000000000011111000000000000000",
            ['.'] = "00000000000000000000000000110001100", [':'] = "00000011000110000000011000110000000",
            ['('] = "00010001000100001000010000010000010", [')'] = "01000001000001000010000100010001000",
            ['/'] = "00001000010001000100010001000010000", [','] = "00000000000000000000011000010001000",
            ['+'] = "00000001000010011111001000010000000", [' '] = "00000000000000000000000000000000000",
            ['?'] = "01110100010000100010001000000000100",
        };

        /// <summary>True when the 5 x 7 glyph of <paramref name="c"/> has its pixel at column <paramref name="col"/>, row <paramref name="row"/> (row 0 = top) set.</summary>
        public static bool Pixel(char c, int col, int row)
        {
            if (col < 0 || col >= W || row < 0 || row >= H) return false;
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(c), out var g)) g = Glyphs['?'];
            return g[row * W + col] == '1';
        }

        /// <summary>Pixel width of a string at a scale (1 px gap between glyphs).</summary>
        public static int Measure(string text, int scale) => string.IsNullOrEmpty(text) ? 0 : (text.Length * (W + 1) - 1) * scale;

        /// <summary>Draws text into a pixel buffer (row 0 = bottom, as Texture2D stores it); (x, y) is the top-left corner.</summary>
        public static void Draw(Color32[] pixels, int width, int height, string text, int x, int y, int scale, Color32 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int cx = x;
            foreach (char raw in text.ToUpperInvariant())
            {
                if (!Glyphs.TryGetValue(raw, out var g)) g = Glyphs['?'];
                for (int row = 0; row < H; row++)
                    for (int col = 0; col < W; col++)
                    {
                        if (g[row * W + col] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = cx + col * scale + sx;
                                int py = height - 1 - (y + row * scale + sy);
                                if (px >= 0 && px < width && py >= 0 && py < height) pixels[py * width + px] = color;
                            }
                    }
                cx += (W + 1) * scale;
            }
        }
    }
}
