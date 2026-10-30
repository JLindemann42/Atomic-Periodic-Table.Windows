// Emits shared/golden/tokens.tsv and script_probe.tsv (the shared-data submodule).
//
// This is the tripwire for the whole engine port. UnicodeScripts and JavaRegex are the two places
// where a mistake compiles, runs, throws nothing, and quietly produces a different index from the
// one Android built - so recall drops and no test notices. Dumping what the JVM actually does,
// and asserting the C# port reproduces it byte for byte, converts "did the Devanagari range come
// out right?" from unanswerable into a red test.
//
// Tokenizer.kt is reimplemented here rather than called, because calling it would need the whole
// Android module on the classpath. It is a transcription of that file and nothing else; if the
// two ever disagree the golden file is wrong and must be regenerated from the real one.
//
// Run with Android Studio's JBR, the same JDK Gradle runs the Android unit tests on:
//   "C:\Program Files\Android\Android Studio\jbr\bin\java.exe" DumpTokenizerGolden.java <outDir>

import java.io.IOException;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.text.Normalizer;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class DumpTokenizerGolden {

    // --- transcription of ai/retrieval/Tokenizer.kt ---------------------------------------

    private static final List<String> LATIN_SUFFIXES = List.of("es", "en", "er", "s", "e");

    private static final Map<Character, Character> ARABIC_FOLD = new LinkedHashMap<>();
    static {
        ARABIC_FOLD.put('\u064A', '\u06CC'); // ي -> ی
        ARABIC_FOLD.put('\u0643', '\u06A9'); // ك -> ک
        ARABIC_FOLD.put('\u0647', '\u06C1'); // ه -> ہ
        ARABIC_FOLD.put('\u0623', '\u0627'); // أ -> ا
        ARABIC_FOLD.put('\u0625', '\u0627'); // إ -> ا
        ARABIC_FOLD.put('\u0622', '\u0627'); // آ -> ا
        ARABIC_FOLD.put('\u0671', '\u0627'); // ٱ -> ا
    }

    private static boolean isArabicDiacritic(char c) {
        return (c >= '\u064B' && c <= '\u0652') || c == '\u0670' || c == '\u200C' || c == '\u0640';
    }

    static List<String> tokenize(String text, String language) {
        List<String> tokens = new ArrayList<>();
        if (text.isBlank()) return tokens;

        StringBuilder buffer = new StringBuilder();
        StringBuilder hanRun = new StringBuilder();

        for (char raw : normalize(text).toCharArray()) {
            if (isHan(raw)) {
                flushLatin(buffer, language, tokens);
                hanRun.append(raw);
            } else if (Character.isLetterOrDigit(raw) || isCombiningMark(raw)) {
                flushHan(hanRun, tokens);
                buffer.append(raw);
            } else {
                flushHan(hanRun, tokens);
                flushLatin(buffer, language, tokens);
            }
        }
        flushHan(hanRun, tokens);
        flushLatin(buffer, language, tokens);
        return tokens;
    }

    private static void flushLatin(StringBuilder buffer, String language, List<String> tokens) {
        if (buffer.length() == 0) return;
        emitAlphabetic(buffer.toString(), language, tokens);
        buffer.setLength(0);
    }

    private static void flushHan(StringBuilder hanRun, List<String> tokens) {
        if (hanRun.length() == 0) return;
        String run = hanRun.toString();
        for (char c : run.toCharArray()) tokens.add(String.valueOf(c));
        for (int i = 0; i < run.length() - 1; i++) tokens.add(run.substring(i, i + 2));
        hanRun.setLength(0);
    }

    private static void emitAlphabetic(String token, String language, List<String> into) {
        if (token.isEmpty()) return;
        String cleaned = isArabicScript(token) ? foldArabic(token) : token;
        if (cleaned.isEmpty()) return;
        into.add(cleaned);
        String folded = foldLatinSuffix(cleaned);
        if (!folded.equals(cleaned)) into.add(folded);
    }

    static String normalize(String text) {
        String nfkc = Normalizer.normalize(text, Normalizer.Form.NFKC).toLowerCase();
        String decomposed = Normalizer.normalize(nfkc, Normalizer.Form.NFD);
        StringBuilder out = new StringBuilder(decomposed.length());
        boolean lastBaseWasLatin = false;
        for (char c : decomposed.toCharArray()) {
            if (isArabicDiacritic(c)) continue;
            if (Character.getType(c) == Character.NON_SPACING_MARK) {
                if (lastBaseWasLatin) continue;
                out.append(c);
                continue;
            }
            lastBaseWasLatin = Character.UnicodeScript.of(c) == Character.UnicodeScript.LATIN;
            out.append(c);
        }
        return out.toString();
    }

    static String foldLatinSuffix(String token) {
        if (token.length() <= 5) return token;
        for (char c : token.toCharArray()) if (Character.isDigit(c)) return token;
        for (String suffix : LATIN_SUFFIXES) {
            if (token.length() - suffix.length() >= 4 && token.endsWith(suffix)) {
                return token.substring(0, token.length() - suffix.length());
            }
        }
        return token;
    }

    private static String foldArabic(String token) {
        StringBuilder out = new StringBuilder(token.length());
        for (char c : token.toCharArray()) {
            if (isArabicDiacritic(c)) continue;
            out.append(ARABIC_FOLD.getOrDefault(c, c));
        }
        return out.toString();
    }

    static boolean isHan(char c) {
        return Character.UnicodeScript.of(c) == Character.UnicodeScript.HAN;
    }

    private static boolean isCombiningMark(char c) {
        int t = Character.getType(c);
        return t == Character.NON_SPACING_MARK
            || t == Character.COMBINING_SPACING_MARK
            || t == Character.ENCLOSING_MARK;
    }

    private static boolean isArabicScript(String token) {
        for (char c : token.toCharArray()) {
            if (Character.UnicodeScript.of(c) == Character.UnicodeScript.ARABIC) return true;
        }
        return false;
    }

    // --- fixture --------------------------------------------------------------------------

    // Chosen to exercise every branch that differs between the JVM and .NET: accent folding vs
    // Devanagari matras, Arabic orthography variants and harakat, Han runs and their bigrams,
    // digits and units inside formulas, and the suffix fold's length boundaries.
    private static final String[][] CASES = {
        {"en", "What is the density of iron?"},
        {"en", "densities of the elements"},
        {"en", "IRON"},
        {"en", "7.87 g/cm3"},
        {"en", "1.00784 (u)"},
        {"en", "Fe2(SO4)3"},
        {"en", "2.8 x 10^10"},
        {"en", "160-350 (MPa)"},
        {"en", "cafe\u0301 na\u00efve r\u00e9sum\u00e9"},
        {"en", "\uFF28\uFF25\uFF2C\uFF2C\uFF2F"},                      // fullwidth, NFKC folds it
        {"en", "a"},
        {"en", "abcde"},
        {"en", "abcdef"},
        {"en", "houses"},
        {"en", "kitchen"},
        {"en", "silver"},
        {"en", ""},
        {"en", "   "},
        {"sv", "V\u00e4tgas \u00e4r l\u00e4ttast"},
        {"sv", "st\u00f6rre \u00e4n"},
        {"de", "Dichte von Eisen"},
        {"de", "Schmelzpunkt"},
        {"fr", "\u00e9l\u00e9ment chimique"},
        {"es", "n\u00famero at\u00f3mico"},
        {"pt", "densidade do ferro"},
        {"it", "punto di fusione"},
        {"af", "digtheid van yster"},
        {"fil", "densidad ng bakal"},
        {"hi", "\u0932\u094b\u0939\u0947 \u0915\u093e \u0918\u0928\u0924\u094d\u0935"},     // लोहे का घनत्व
        {"hi", "\u0918\u0928\u0924\u094d\u0935"},                                           // घनत्व
        {"hi", "\u092a\u0930\u092e\u093e\u0923\u0941 \u0938\u0902\u0916\u094d\u092f\u093e"},
        {"hi", "\u0967\u0968\u0969"},                                                       // Devanagari digits
        {"ur", "\u0644\u0648\u06c1\u06d2 \u06a9\u06cc \u06a9\u062b\u0627\u0641\u062a"},
        {"ur", "\u0643\u062b\u0627\u0641\u062a"},                     // Arabic kaf, folds to keheh
        {"ur", "\u064e\u0643\u064e\u062b\u064e\u0627\u0641\u064e\u062a"}, // with harakat
        {"ur", "\u0623\u0644\u0625\u0622\u0671"},                     // hamza forms fold to alef
        {"ur", "\u0669\u0668\u0667"},                                 // Arabic-Indic digits
        {"zh", "\u94c1\u7684\u5bc6\u5ea6"},                           // 铁的密度
        {"zh", "\u8fc7\u6e21\u91d1\u5c5e"},                           // 过渡金属
        {"zh", "\u91d1"},
        {"zh", "\u94c1 iron \u5bc6\u5ea6"},                           // mixed Han and Latin
        {"en", "H\u2082O and CO\u2082"},                              // subscripts, NFKC folds
        {"en", "alpha \u03b1 beta \u03b2"},                           // Greek
        {"ru", "\u043f\u043b\u043e\u0442\u043d\u043e\u0441\u0442\u044c"}, // Cyrillic, unsupported lang
        {"en", "iron\u00b7carbon"},
        {"en", "m\u00b3/mol"},
        {"en", "\uD83D\uDE00 emoji"},                                 // surrogate pair
    };

    public static void main(String[] args) throws IOException {
        Path outDir = Path.of(args.length > 0 ? args[0] : ".");
        Files.createDirectories(outDir);

        try (PrintWriter w = new PrintWriter(
                Files.newBufferedWriter(outDir.resolve("tokens.tsv"), StandardCharsets.UTF_8))) {
            w.println("# generated by tools/port/DumpTokenizerGolden.java on JDK "
                    + System.getProperty("java.version"));
            w.println("# language\tinput\ttoken\ttoken...  (fields tab separated, input escaped)");
            for (String[] c : CASES) {
                List<String> tokens = tokenize(c[1], c[0]);
                w.println(c[0] + "\t" + escape(c[1])
                        + (tokens.isEmpty() ? "" : "\t" + String.join("\t", tokens)));
            }
        }

        // Every distinct character in the fixture, with the three script answers the tokenizer
        // actually asks for. Narrows a golden failure to the exact character.
        try (PrintWriter w = new PrintWriter(
                Files.newBufferedWriter(outDir.resolve("script_probe.tsv"), StandardCharsets.UTF_8))) {
            w.println("# codepoint\tscript\tisLetterOrDigit\tgetType");
            java.util.TreeSet<Character> chars = new java.util.TreeSet<>();
            for (String[] c : CASES) {
                for (char ch : c[1].toCharArray()) chars.add(ch);
                for (char ch : normalize(c[1]).toCharArray()) chars.add(ch);
            }
            for (char ch : chars) {
                w.printf("U+%04X\t%s\t%b\t%d%n", (int) ch,
                        Character.UnicodeScript.of(ch).name(),
                        Character.isLetterOrDigit(ch),
                        Character.getType(ch));
            }
        }

        System.err.println("wrote tokens.tsv (" + CASES.length + " cases) and script_probe.tsv");
    }

    private static String escape(String s) {
        return s.replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "\\r");
    }
}
