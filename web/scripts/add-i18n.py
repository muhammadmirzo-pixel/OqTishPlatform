"""Dev helper: append keys to an i18n section file (uz, ru, en blocks end with '} as const' / '}').
Usage: python3 scripts/add-i18n.py <section.ts> <json file with {"uz": {...}, "ru": {...}, "en": {...}}>"""
import json, re, sys

path, data = sys.argv[1], json.load(open(sys.argv[2]))
s = open(path).read()
markers = [('uz', '\n} as const\n'), ('ru', None), ('en', None)]
# Find the closing brace of each of the three object literals in order.
idx = 0
for lang in ['uz', 'ru', 'en']:
    m = re.compile(r'\n}( as const)?\n').search(s, idx)
    lines = ''.join(f"  {json.dumps(k, ensure_ascii=False).replace(chr(34), chr(39)) if chr(39) not in k else json.dumps(k)}: {json.dumps(v, ensure_ascii=False)},\n" for k, v in data[lang].items())
    s = s[:m.start() + 1] + lines + s[m.start() + 1:]
    idx = m.start() + 1 + len(lines) + 2
open(path, 'w').write(s)
