// ZMK Keymap Visualizer — renders one PNG per layer for a Corne split keyboard.
// Usage: keymap-visualizer <path-to-.keymap-file> [output-directory]

using System.Text;
using SkiaSharp;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: keymap-visualizer <path-to-.keymap> [output-dir]");
    return 1;
}

string keymapPath = args[0];
string outputDir  = args.Length >= 2 ? args[1] : Path.GetDirectoryName(Path.GetFullPath(keymapPath))!;

if (!File.Exists(keymapPath))
{
    Console.Error.WriteLine($"File not found: {keymapPath}");
    return 1;
}

Directory.CreateDirectory(outputDir);

string source = File.ReadAllText(keymapPath);
var layers     = KeymapParser.Parse(source);

if (layers.Count == 0)
{
    Console.Error.WriteLine("No layers found in keymap.");
    return 1;
}

foreach (var layer in layers)
{
    string outPath = Path.Combine(outputDir, $"layer_{layer.Index}_{layer.Name}.png");
    CorneRenderer.Render(layer, outPath);
    Console.WriteLine($"Written: {outPath}");
}

return 0;

// ══════════════════════════════════════════════════════════════════════════════
// Data model
// ══════════════════════════════════════════════════════════════════════════════

record KeyDef(string Raw, string Label, string? SubLabel = null);

record Layer(int Index, string Name, List<KeyDef> Keys);

// ══════════════════════════════════════════════════════════════════════════════
// Parser
// ══════════════════════════════════════════════════════════════════════════════

static class KeymapParser
{
    // Matches a complete layer block:  name { ... bindings = < ... >; ... };
    // Matches a layer block inside the keymap scope: name { ... bindings = < ... >; ... };
    private static readonly System.Text.RegularExpressions.Regex LayerRe = new(
        @"(\w+)\s*\{[^{}]*bindings\s*=\s*<([\s\S]*?)>\s*;[^{}]*\}",
        System.Text.RegularExpressions.RegexOptions.Multiline);

    // Tokenises the bindings stream into individual &xxx ... entries
    private static readonly System.Text.RegularExpressions.Regex TokenRe = new(
        @"&\S+(?:\s+[^&<>\s][^&<>\s]*)*",
        System.Text.RegularExpressions.RegexOptions.Multiline);

    public static List<Layer> Parse(string source)
    {
        // Strip C-style block and line comments
        source = System.Text.RegularExpressions.Regex.Replace(source, @"/\*[\s\S]*?\*/", " ");
        source = System.Text.RegularExpressions.Regex.Replace(source, @"//[^\n]*",       " ");

        // Isolate only the body of the `keymap { compatible = "zmk,keymap"; ... }` block
        // so we never accidentally match behavior or tap-dance blocks.
        string keymapBody = ExtractKeymapBody(source);
        if (string.IsNullOrEmpty(keymapBody))
        {
            Console.Error.WriteLine("Warning: could not locate a 'keymap' block; scanning entire file.");
            keymapBody = source;
        }

        var layers = new List<Layer>();
        int index  = 0;

        foreach (System.Text.RegularExpressions.Match lm in LayerRe.Matches(keymapBody))
        {
            string layerName    = lm.Groups[1].Value;
            string bindingsText = lm.Groups[2].Value;

            // A layer must have at least 10 bindings (safety guard)
            var rawTokens = TokenRe.Matches(bindingsText);
            if (rawTokens.Count < 10) continue;

            var keys = new List<KeyDef>();
            foreach (System.Text.RegularExpressions.Match tm in rawTokens)
            {
                string raw = tm.Value.Trim();
                // Normalise inner whitespace
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ");
                keys.Add(ParseBinding(raw));
            }

            layers.Add(new Layer(index++, layerName, keys));
        }

        return layers;
    }

    /// <summary>
    /// Extracts the content inside the outermost `keymap { ... }` block using a
    /// brace-depth counter so nested braces are handled correctly.
    /// </summary>
    private static string ExtractKeymapBody(string source)
    {
        // Find the `keymap {` opener (the node whose compatible is "zmk,keymap")
        var headerRe = new System.Text.RegularExpressions.Regex(
            @"\bkeymap\s*\{",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        var headerMatch = headerRe.Match(source);
        if (!headerMatch.Success) return string.Empty;

        int start = headerMatch.Index + headerMatch.Length; // position right after the opening brace
        int depth = 1;
        int i     = start;

        while (i < source.Length && depth > 0)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            i++;
        }

        if (depth != 0) return string.Empty; // unbalanced braces

        // Return everything between the opening `{` and the matching `}`
        return source[start..(i - 1)];
    }

    private static KeyDef ParseBinding(string raw)
    {
        // Split on spaces; first token is the behaviour (&kp, &mo, &bt, &trans …)
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string behaviour = parts[0].TrimStart('&').ToLowerInvariant();

        switch (behaviour)
        {
            case "kp":
                if (parts.Length >= 2)
                {
                    string label = Lookup(parts[1]);
                    return new KeyDef(raw, label);
                }
                break;

            case "trans":
                return new KeyDef(raw, "---");

            case "none":
                return new KeyDef(raw, "xxx");

            case "mo":
                if (parts.Length >= 2)
                    return new KeyDef(raw, $"MO", parts[1]);
                break;

            case "lt":
                // &lt <layer> <key>
                if (parts.Length >= 3)
                    return new KeyDef(raw, Lookup(parts[2]), $"LT{parts[1]}");
                if (parts.Length >= 2)
                    return new KeyDef(raw, $"LT{parts[1]}");
                break;

            case "mt":
            case "ht_tp":
            {
                // &mt <mod> <tap-key>  /  &ht_tp <hold-key> <tap-key>
                // Primary (large) label = tap action; sub-label = hold action
                if (parts.Length >= 3)
                {
                    string tapLabel  = Lookup(parts[2]);
                    string holdLabel = Lookup(parts[1]);
                    return new KeyDef(raw, tapLabel, holdLabel);
                }
                break;
            }

            case "sk":
                if (parts.Length >= 2)
                    return new KeyDef(raw, $"SK", Lookup(parts[1]));
                break;

            case "sl":
                if (parts.Length >= 2)
                    return new KeyDef(raw, "SL", parts[1]);
                break;

            case "to":
                if (parts.Length >= 2)
                    return new KeyDef(raw, "TO", parts[1]);
                break;

            case "tog":
                if (parts.Length >= 2)
                    return new KeyDef(raw, "TOG", parts[1]);
                break;

            case "bt":
            {
                // &bt BT_SEL 0, &bt BT_CLR, …
                if (parts.Length >= 3 && parts[1].ToUpperInvariant() == "BT_SEL")
                    return new KeyDef(raw, "BT", parts[2]);
                if (parts.Length >= 2)
                    return new KeyDef(raw, BtLabel(parts[1]));
                break;
            }

            case "out":
                if (parts.Length >= 2)
                    return new KeyDef(raw, OutLabel(parts[1]));
                break;

            case "reset":
                return new KeyDef(raw, "RESET");

            case "bootloader":
                return new KeyDef(raw, "BOOT");

            case "rgb_ug":
                return new KeyDef(raw, "RGB", parts.Length >= 2 ? RgbLabel(parts[1]) : null);

            case "ext_power":
                return new KeyDef(raw, "PWR", parts.Length >= 2 ? parts[1] : null);

            case "td_sqt":
                return new KeyDef(raw, "' `");

            default:
                // Custom tap-dance or unknown: show behaviour name
                if (behaviour.StartsWith("td_"))
                    return new KeyDef(raw, behaviour[3..].ToUpperInvariant());
                break;
        }

        // Fallback: strip & and display raw
        return new KeyDef(raw, raw.TrimStart('&').ToUpperInvariant().Replace("_", " "));
    }

    private static string BtLabel(string cmd) => cmd.ToUpperInvariant() switch
    {
        "BT_CLR"      => "BT CLR",
        "BT_CLR_ALL"  => "BT CLR ALL",
        "BT_NXT"      => "BT NXT",
        "BT_PRV"      => "BT PRV",
        _             => cmd
    };

    private static string OutLabel(string cmd) => cmd.ToUpperInvariant() switch
    {
        "OUT_USB" => "USB",
        "OUT_BLE" => "BLE",
        "OUT_TOG" => "OUT TOG",
        _         => cmd
    };

    private static string RgbLabel(string cmd) => cmd.ToUpperInvariant() switch
    {
        "RGB_TOG" => "TOG",
        "RGB_BRI" => "BRI+",
        "RGB_BRD" => "BRI-",
        "RGB_SAI" => "SAT+",
        "RGB_SAD" => "SAT-",
        "RGB_HUI" => "HUE+",
        "RGB_HUD" => "HUE-",
        "RGB_EFF" => "EFF+",
        "RGB_EFR" => "EFF-",
        _         => cmd
    };

    // ── Key-code → human-readable label ──────────────────────────────────────

    private static string Lookup(string code)
    {
        // Strip modifier wrappers like LS(...), LC(...), RA(...), RS(...)
        var modMatch = System.Text.RegularExpressions.Regex.Match(code,
            @"^(LS|RS|LC|RC|LA|RA|LG|RG)\((.+)\)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (modMatch.Success)
        {
            string modPrefix = modMatch.Groups[1].Value.ToUpperInvariant();
                string inner     = Lookup(modMatch.Groups[2].Value);
                return $"{ModPrefixLabel(modPrefix)}{inner}";
        }

        string upper = code.ToUpperInvariant();

        // SV_ aliases — normalise to their glyph
        if (upper.StartsWith("SV_") || SvAliases.TryGetValue(upper, out _))
        {
            if (SvAliases.TryGetValue(upper, out string? sv)) return sv;
        }

        if (KeyLabels.TryGetValue(upper, out string? label)) return label;

        // Strip common prefixes for display
        foreach (string prefix in new[] { "NUMBER_", "N", "F" })
        {
            if (upper.StartsWith(prefix))
            {
                string rest = upper[prefix.Length..];
                if (int.TryParse(rest, out int n))
                    return prefix == "F" ? $"F{n}" : $"{n}";
            }
        }

        // Return the raw code but make it prettier
        return upper
            .Replace("LEFT_",  "L")
            .Replace("RIGHT_", "R")
            .Replace("_AND_",  "/")
            .Replace("_",      " ");
    }

    private static string ModPrefixLabel(string p) => p switch
    {
        "LS" or "RS" => "S+",
        "LC" or "RC" => "C+",
        "LA" or "RA" => "A+",
        "LG" or "RG" => "G+",
        _            => p + "+"
    };

    // ── Lookup tables ─────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> SvAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SV_EXCLAMATION"]  = "!",  ["SV_EXCL"]    = "!",
        ["SV_DOUBLE_QUOTES"]= "\"", ["SV_DQT"]     = "\"",
        ["SV_HASH"]         = "#",  ["SV_POUND"]   = "#",
        ["SV_DOLLAR"]       = "$",  ["SV_DLLR"]    = "$",
        ["SV_PERCENT"]      = "%",  ["SV_PRCNT"]   = "%",
        ["SV_AMPERSAND"]    = "&",  ["SV_AMPS"]    = "&",
        ["SV_SINGLE_QUOTE"] = "'",  ["SV_SQT"]     = "'",
        ["SV_APOSTROPHE"]   = "'",  ["SV_APOS"]    = "'",
        ["SV_LEFT_PARENTHESIS"] = "(", ["SV_LPAR"] = "(",
        ["SV_RIGHT_PARENTHESIS"]= ")", ["SV_RPAR"] = ")",
        ["SV_ASTERISK"]     = "*",  ["SV_ASTRK"]   = "*",  ["SV_STAR"] = "*",
        ["SV_PLUS"]         = "+",
        ["SV_COMMA"]        = ",",
        ["SV_MINUS"]        = "-",
        ["SV_PERIOD"]       = ".",  ["SV_DOT"]     = ".",
        ["SV_SLASH"]        = "/",  ["SV_FSLH"]    = "/",
        ["SV_COLON"]        = ":",
        ["SV_SEMICOLON"]    = ";",  ["SV_SEMI"]    = ";",
        ["SV_LESS_THAN"]    = "<",  ["SV_LT"]      = "<",
        ["SV_EQUAL"]        = "=",
        ["SV_GREATER_THAN"] = ">",  ["SV_GT"]      = ">",
        ["SV_QUESTION"]     = "?",  ["SV_QMARK"]   = "?",
        ["SV_AT_SIGN"]      = "@",  ["SV_AT"]      = "@",
        ["SV_LEFT_BRACKET"] = "[",  ["SV_LBKT"]    = "[",
        ["SV_BACKSLASH"]    = "\\", ["SV_BSLH"]    = "\\",
        ["SV_RIGHT_BRACKET"]= "]",  ["SV_RBKT"]    = "]",
        ["SV_CARET"]        = "^",
        ["SV_UNDERSCORE"]   = "_",  ["SV_UNDER"]   = "_",
        ["SV_GRAVE"]        = "`",
        ["SV_LEFT_BRACE"]   = "{",  ["SV_LBRC"]    = "{",
        ["SV_PIPE"]         = "|",
        ["SV_RIGHT_BRACE"]  = "}",  ["SV_RBRC"]    = "}",
        ["SV_TILDE"]        = "~",
        ["SV_ACUTE"]        = "´",
        ["SV_A_UMLAUT"]     = "Ä",
        ["SV_A_RING"]       = "Å",
        ["SV_O_UMLAUT"]     = "Ö",
        ["SV_EURO"]         = "€",
        ["SV_POUND_SIGN"]   = "£",
        ["SV_CURRENCY_SIGN"]= "¤",  ["SV_CURREN"]  = "¤",
        ["SV_SECTION"]      = "§",  ["SV_SECT"]    = "§",
        ["SV_UMLAUT"]       = "¨",
        ["SV_MU"]           = "µ",  ["SV_MICRO"]   = "µ",
        ["SV_ONE_HALF"]     = "½",  ["SV_FRAC_1_2"]= "½",
    };

    private static readonly Dictionary<string, string> KeyLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters
        ["A"] = "A", ["B"] = "B", ["C"] = "C", ["D"] = "D",
        ["E"] = "E", ["F"] = "F", ["G"] = "G", ["H"] = "H",
        ["I"] = "I", ["J"] = "J", ["K"] = "K", ["L"] = "L",
        ["M"] = "M", ["N"] = "N", ["O"] = "O", ["P"] = "P",
        ["Q"] = "Q", ["R"] = "R", ["S"] = "S", ["T"] = "T",
        ["U"] = "U", ["V"] = "V", ["W"] = "W", ["X"] = "X",
        ["Y"] = "Y", ["Z"] = "Z",
        // Numbers (bare & N-prefixed)
        ["N0"] = "0", ["N1"] = "1", ["N2"] = "2", ["N3"] = "3", ["N4"] = "4",
        ["N5"] = "5", ["N6"] = "6", ["N7"] = "7", ["N8"] = "8", ["N9"] = "9",
        ["NUMBER_0"] = "0", ["NUMBER_1"] = "1", ["NUMBER_2"] = "2",
        ["NUMBER_3"] = "3", ["NUMBER_4"] = "4", ["NUMBER_5"] = "5",
        ["NUMBER_6"] = "6", ["NUMBER_7"] = "7", ["NUMBER_8"] = "8",
        ["NUMBER_9"] = "9",
        // Punctuation / symbols
        ["GRAVE"]           = "`",
        ["TILDE"]           = "~",
        ["EXCL"]            = "!",
        ["AT"]              = "@",
        ["HASH"]            = "#",
        ["DLLR"]            = "$",
        ["DOLLAR"]          = "$",
        ["PRCNT"]           = "%",
        ["PERCENT"]         = "%",
        ["CARET"]           = "^",
        ["AMPS"]            = "&",
        ["AMPERSAND"]       = "&",
        ["ASTRK"]           = "*",
        ["ASTERISK"]        = "*",
        ["STAR"]            = "*",
        ["LPAR"]            = "(",
        ["LEFT_PARENTHESIS"]= "(",
        ["RPAR"]            = ")",
        ["RIGHT_PARENTHESIS"]= ")",
        ["MINUS"]           = "-",
        ["UNDER"]           = "_",
        ["UNDERSCORE"]      = "_",
        ["PLUS"]            = "+",
        ["EQUAL"]           = "=",
        ["LBKT"]            = "[",
        ["LEFT_BRACKET"]    = "[",
        ["RBKT"]            = "]",
        ["RIGHT_BRACKET"]   = "]",
        ["LBRC"]            = "{",
        ["LEFT_BRACE"]      = "{",
        ["RBRC"]            = "}",
        ["RIGHT_BRACE"]     = "}",
        ["BSLH"]            = "\\",
        ["BACKSLASH"]       = "\\",
        ["PIPE"]            = "|",
        ["SEMI"]            = ";",
        ["SEMICOLON"]       = ";",
        ["COLON"]           = ":",
        ["SQT"]             = "'",
        ["SINGLE_QUOTE"]    = "'",
        ["APOSTROPHE"]      = "'",
        ["DQT"]             = "\"",
        ["DOUBLE_QUOTES"]   = "\"",
        ["COMMA"]           = ",",
        ["LT"]              = "<",
        ["LESS_THAN"]       = "<",
        ["DOT"]             = ".",
        ["PERIOD"]          = ".",
        ["GT"]              = ">",
        ["GREATER_THAN"]    = ">",
        ["FSLH"]            = "-",
        ["SLASH"]           = "-",
        ["QMARK"]           = "?",
        ["QUESTION"]        = "?",
        // Function keys
        ["F1"]  = "F1",  ["F2"]  = "F2",  ["F3"]  = "F3",  ["F4"]  = "F4",
        ["F5"]  = "F5",  ["F6"]  = "F6",  ["F7"]  = "F7",  ["F8"]  = "F8",
        ["F9"]  = "F9",  ["F10"] = "F10", ["F11"] = "F11", ["F12"] = "F12",
        ["F13"] = "F13", ["F14"] = "F14", ["F15"] = "F15", ["F16"] = "F16",
        ["F17"] = "F17", ["F18"] = "F18", ["F19"] = "F19", ["F20"] = "F20",
        // Modifiers
        ["LSHFT"]    = "Sft", ["RSHFT"]    = "Sft",
        ["LSHIFT"]   = "Sft", ["RSHIFT"]   = "Sft",
        ["LCTRL"]    = "Ctl", ["RCTRL"]    = "Ctl",
        ["LALT"]     = "Alt", ["RALT"]     = "Alt",
        ["LGUI"]     = "GUI", ["RGUI"]     = "GUI",
        ["LMETA"]    = "GUI", ["RMETA"]    = "GUI",
        ["LWIN"]     = "Win", ["RWIN"]     = "Win",
        // Special keys
        ["RET"]      = "Ret", ["ENTER"]    = "Ret",  ["RETURN"] = "Ret",
        ["ESC"]      = "Esc", ["ESCAPE"]   = "Esc",
        ["BSPC"]     = "Bsp", ["BACKSPACE"]= "Bsp",
        ["DEL"]      = "Del", ["DELETE"]   = "Del",
        ["TAB"]      = "Tab",
        ["SPACE"]    = "Spc",
        ["CAPS"]     = "Caps",["CLCK"]     = "Caps",
        // Navigation
        ["LEFT"]     = "<-",  ["RIGHT"]    = "->",
        ["UP"]       = "Up",  ["DOWN"]     = "Dn",
        ["PG_UP"]    = "PgUp", ["PAGE_UP"]   = "PgUp",
        ["PG_DN"]    = "PgDn", ["PAGE_DOWN"] = "PgDn",
        ["HOME"]     = "Home",["END"]     = "End",
        ["INS"]      = "Ins", ["INSERT"]  = "Ins",
        // Numpad
        ["KP_N0"] = "K0", ["KP_N1"] = "K1", ["KP_N2"] = "K2", ["KP_N3"] = "K3",
        ["KP_N4"] = "K4", ["KP_N5"] = "K5", ["KP_N6"] = "K6", ["KP_N7"] = "K7",
        ["KP_N8"] = "K8", ["KP_N9"] = "K9",
        ["KP_DOT"]    = "K.", ["KP_COMMA"] = "K,",
        ["KP_PLUS"]   = "K+", ["KP_MINUS"] = "K-",
        ["KP_MULTIPLY"]= "K*",["KP_DIVIDE"]= "K/",
        ["KP_ENTER"]  = "K↩", ["KP_EQUAL"] = "K=",
        // Media / system
        ["C_MUTE"]       = "Mute", ["K_MUTE"]       = "Mute",
        ["C_VOL_UP"]     = "Vol+", ["C_VOL_DN"]     = "Vol-",
        ["C_PP"]         = "Play", ["C_PLAY_PAUSE"] = "Play",
        ["C_NEXT"]       = "Next", ["C_PREV"]       = "Prev",
        ["C_STOP"]       = "Stop",
        ["C_BRI_UP"]     = "Bri+", ["C_BRI_DN"]     = "Bri-",
        ["PRINTSCREEN"]  = "PrtSc", ["PSCRN"] = "PrtSc",
        ["SCROLLLOCK"]   = "ScrLk",
        ["PAUSE_BREAK"]  = "Pause",
        ["K_APP"]        = "Menu",
        ["C_AL_CALC"]    = "Calc",
        ["C_AL_WWW"]     = "WWW",
        // Bluetooth (when used directly as keys — usually via &bt)
        ["BT_CLR"]       = "BT CLR",
        ["BT_NXT"]       = "BT NXT",
        ["BT_PRV"]       = "BT PRV",
        // trans / none rendered as text fallback
        ["TRANS"]        = "---",
        ["NONE"]         = "xxx",
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// Renderer
// ══════════════════════════════════════════════════════════════════════════════

static class CorneRenderer
{
    // ── Layout constants ──────────────────────────────────────────────────────
    // Corne: 3 rows × 6 cols per half + 3 thumb keys per half = 42 keys total
    // Key indices (0-based, row-major, left half first):
    //   Row 0 : 0-5   (left)  6-11  (right)
    //   Row 1 : 12-17 (left)  18-23 (right)
    //   Row 2 : 24-29 (left)  30-35 (right)
    //   Thumbs: 36-38 (left)  39-41 (right)

    private const float KeySize    = 72f;   // key square side
    private const float KeyGap     = 6f;    // gap between keys
    private const float KeyRadius  = 8f;    // rounded corner radius
    private const float HalfGap    = 40f;   // gap between left and right halves
    private const float MarginX    = 48f;
    private const float MarginY    = 64f;
    private const float ThumbOffY  = 24f;   // extra vertical offset for thumb cluster
    private const float ThumbOffX  = 2 * (KeySize + KeyGap); // thumb cluster starts at col 3

    private const int ColsPerHalf  = 6;
    private const int Rows         = 3;
    private const int ThumbsPerHalf= 3;

    private static float Step => KeySize + KeyGap;

    // Total canvas size
    private static float TotalWidth  => MarginX * 2 + ColsPerHalf * 2 * Step - KeyGap + HalfGap;
    private static float TotalHeight => MarginY * 2 + Rows * Step - KeyGap + ThumbOffY + Step + 48f /* title */;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly SKColor ColBackground = SKColor.Parse("#1E1E2E");
    private static readonly SKColor ColSurface    = SKColor.Parse("#313244");
    private static readonly SKColor ColBorder     = SKColor.Parse("#45475A");
    private static readonly SKColor ColKeyBg      = SKColor.Parse("#45475A");
    private static readonly SKColor ColKeyBgTrans = SKColor.Parse("#2A2A3E");
    private static readonly SKColor ColKeyBgMo    = SKColor.Parse("#7C3AED");
    private static readonly SKColor ColKeyBgBt    = SKColor.Parse("#0E7490");
    private static readonly SKColor ColKeyBgMod   = SKColor.Parse("#1D4ED8");
    private static readonly SKColor ColLabel      = SKColor.Parse("#CDD6F4");
    private static readonly SKColor ColSubLabel   = SKColor.Parse("#A6ADC8");
    private static readonly SKColor ColTransLabel = SKColor.Parse("#585B70");

    // Font resolution — finds the best typeface for a given string, falling back
    // through a prioritised family list so special characters always render.
    private static readonly string[] FontFamilyPriority =
    [
        "Consolas", "Cascadia Mono", "Cascadia Code", "JetBrains Mono",
        "Fira Code", "DejaVu Sans Mono", "Liberation Mono",
        "Noto Sans Mono", "Segoe UI", "Arial Unicode MS",
        "Noto Sans", "sans-serif"
    ];

    private static SKTypeface ResolveFace(string text, SKFontStyle style)
    {
        var mgr = SKFontManager.Default;
        foreach (string family in FontFamilyPriority)
        {
            var face = mgr.MatchFamily(family, style);
            if (face is null) continue;
            // Check that every code-point in the text has a glyph
            bool allGlyphs = true;
            foreach (Rune r in text.EnumerateRunes())
            {
                if (!face.ContainsGlyph(r.Value)) { allGlyphs = false; break; }
            }
            if (allGlyphs) return face;
        }
        // Last resort: let SkiaSharp pick whatever it can find
        return mgr.MatchCharacter(text[0]) ?? SKTypeface.Default;
    }
    private static readonly SKColor ColTitle      = SKColor.Parse("#CBA6F7");
    private static readonly SKColor ColHalfBg     = SKColor.Parse("#292739");

    public static void Render(Layer layer, string outputPath)
    {
        int width  = (int)TotalWidth;
        int height = (int)TotalHeight;

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        // Background
        canvas.Clear(ColBackground);

        // Draw half backgrounds
        DrawHalfBackground(canvas, isLeft: true);
        DrawHalfBackground(canvas, isLeft: false);

        // Title
        DrawTitle(canvas, layer, width);

        // Keys — ensure we have exactly 42 slots (pad with empty if needed)
        var keys = layer.Keys;
        while (keys.Count < 42) keys.Add(new KeyDef("", ""));

        // Draw main grid rows.
        // ZMK bindings are laid out row-major across the full 12-key row:
        //   Row 0: indices  0-5  (left),  6-11 (right)
        //   Row 1: indices 12-17 (left), 18-23 (right)
        //   Row 2: indices 24-29 (left), 30-35 (right)
        //   Thumbs: 36-38 (left), 39-41 (right)
        int keysPerRow = ColsPerHalf * 2; // 12
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < ColsPerHalf; col++)
            {
                // Left half: first 6 keys of this row
                int leftIdx = row * keysPerRow + col;
                var (lx, ly) = GridPos(row, col, isLeft: true);
                DrawKey(canvas, keys[leftIdx], lx, ly);

                // Right half: last 6 keys of this row
                int rightIdx = row * keysPerRow + ColsPerHalf + col;
                var (rx, ry) = GridPos(row, col, isLeft: false);
                DrawKey(canvas, keys[rightIdx], rx, ry);
            }
        }

        // Draw thumb clusters
        for (int t = 0; t < ThumbsPerHalf; t++)
        {
            var (ltx, lty) = ThumbPos(t, isLeft: true);
            DrawKey(canvas, keys[36 + t], ltx, lty);

            var (rtx, rty) = ThumbPos(t, isLeft: false);
            DrawKey(canvas, keys[39 + t], rtx, rty);
        }

        // Save
        using var image = surface.Snapshot();
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs    = File.OpenWrite(outputPath);
        data.SaveTo(fs);
    }

    // ── Position helpers ──────────────────────────────────────────────────────

    private static (float x, float y) GridPos(int row, int col, bool isLeft)
    {
        float titleH = 48f;
        float y = MarginY + titleH + row * Step;

        if (isLeft)
        {
            // Left half: cols run left-to-right
            float x = MarginX + col * Step;
            return (x, y);
        }
        else
        {
            // Right half: starts after left half + gap
            float halfWidth = ColsPerHalf * Step - KeyGap;
            float rightStart = MarginX + halfWidth + HalfGap;
            float x = rightStart + col * Step;
            return (x, y);
        }
    }

    private static (float x, float y) ThumbPos(int thumb, bool isLeft)
    {
        float titleH  = 48f;
        float baseY   = MarginY + titleH + Rows * Step + ThumbOffY;
        float halfWidth = ColsPerHalf * Step - KeyGap;

        if (isLeft)
        {
            // Left thumbs sit below cols 3-5
            float x = MarginX + ThumbOffX + thumb * Step;
            return (x, baseY);
        }
        else
        {
            // Right thumbs sit below cols 0-2 of the right half
            float rightStart = MarginX + halfWidth + HalfGap;
            float x = rightStart + thumb * Step;
            return (x, baseY);
        }
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private static void DrawHalfBackground(SKCanvas canvas, bool isLeft)
    {
        float titleH    = 48f;
        float halfWidth = ColsPerHalf * Step - KeyGap;
        float halfHeight= Rows * Step - KeyGap + ThumbOffY + Step;

        float x = isLeft
            ? MarginX - KeyGap
            : MarginX + halfWidth + HalfGap - KeyGap;
        float y = MarginY + titleH - KeyGap;

        var rect = new SKRoundRect(new SKRect(x, y, x + halfWidth + KeyGap * 2, y + halfHeight + KeyGap * 2), 12f);
        using var paint = new SKPaint { Color = ColHalfBg, IsAntialias = true };
        canvas.DrawRoundRect(rect, paint);
    }

    private static void DrawTitle(SKCanvas canvas, Layer layer, int canvasWidth)
    {
        string title = $"Layer {layer.Index} — {FormatLayerName(layer.Name)}";

        using var paint = new SKPaint
        {
            Color       = ColTitle,
            IsAntialias = true,
        };
        using var font = new SKFont(ResolveFace(title, SKFontStyle.Bold), 22f);

        float textWidth = font.MeasureText(title, paint);
        float x = (canvasWidth - textWidth) / 2f;
        float y = MarginY + 28f;

        canvas.DrawText(title, x, y, font, paint);
    }

    private static void DrawKey(SKCanvas canvas, KeyDef key, float x, float y)
    {
        // Choose key background colour
        SKColor bgColor = ChooseKeyColor(key);

        // Key body
        var rect = new SKRoundRect(new SKRect(x, y, x + KeySize, y + KeySize), KeyRadius);

        using (var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true })
            canvas.DrawRoundRect(rect, bgPaint);

        // Border
        using (var borderPaint = new SKPaint
        {
            Color       = ColBorder,
            IsAntialias = true,
            IsStroke    = true,
            StrokeWidth = 1.5f
        })
            canvas.DrawRoundRect(rect, borderPaint);

        if (string.IsNullOrWhiteSpace(key.Label)) return;

        bool hasSubLabel = !string.IsNullOrWhiteSpace(key.SubLabel);

        // Main label
        float mainSize  = ChooseFontSize(key.Label, hasSubLabel);
        SKColor labelColor = key.Label == "---" ? ColTransLabel : ColLabel;

        using var mainFont = new SKFont(ResolveFace(key.Label, SKFontStyle.Bold), mainSize);
        using var mainPaint = new SKPaint { Color = labelColor, IsAntialias = true };

        float textWidth = mainFont.MeasureText(key.Label, mainPaint);
        float textX     = x + (KeySize - textWidth) / 2f;
        float textY     = hasSubLabel
            ? y + KeySize * 0.56f
            : y + KeySize * 0.62f;

        canvas.DrawText(key.Label, textX, textY, mainFont, mainPaint);

        // Sub-label (hold action / layer number)
        if (hasSubLabel)
        {
            float subSize = 13f;
            using var subFont  = new SKFont(ResolveFace(key.SubLabel!, SKFontStyle.Normal), subSize);
            using var subPaint = new SKPaint { Color = ColSubLabel, IsAntialias = true };

            float subWidth = subFont.MeasureText(key.SubLabel!, subPaint);
            float subX     = x + (KeySize - subWidth) / 2f;
            float subY     = y + KeySize * 0.82f;

            canvas.DrawText(key.SubLabel!, subX, subY, subFont, subPaint);
        }
    }

    private static SKColor ChooseKeyColor(KeyDef key)
    {
        if (string.IsNullOrWhiteSpace(key.Label)) return ColKeyBgTrans;
        if (key.Label == "---")                   return ColKeyBgTrans;

        string raw = key.Raw.TrimStart('&').ToLowerInvariant();

        if (raw.StartsWith("mo") || raw.StartsWith("lt") || raw.StartsWith("tog") ||
            raw.StartsWith("to ") || raw.StartsWith("sl"))
            return ColKeyBgMo;

        if (raw.StartsWith("bt") || raw.StartsWith("out"))
            return ColKeyBgBt;

        // Modifier-only keys
        if (key.Label is "Sft" or "Ctl" or "Alt" or "GUI" or "Win" || raw.StartsWith("sk"))
            return ColKeyBgMod;

        return ColKeyBg;
    }

    private static float ChooseFontSize(string label, bool hasSubLabel)
    {
        int len = label.Length;
        if (len <= 1)  return hasSubLabel ? 26f : 28f;
        if (len == 2)  return hasSubLabel ? 22f : 24f;
        if (len == 3)  return hasSubLabel ? 18f : 20f;
        if (len <= 5)  return hasSubLabel ? 15f : 16f;
        return hasSubLabel ? 12f : 13f;
    }

    private static string FormatLayerName(string name)
    {
        // Convert snake_case to Title Case
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(name.Replace("_", " "));
    }
}