#!/usr/bin/env python3
"""Check that nothing private is about to be published.

    scripts/leak-check.py <file> [file ...]

Looks for machine names, accounts, addresses and tooling that belong to the
setup this work was done on and have no business in a public repository or in
a post to an issue.

Two things it does that a grep does not: it ignores the base64 payload inside
an embedded image, where random letters produce false hits and drown the real
ones; and it exits non-zero, so it can gate a publish rather than be read.
"""
import re
import sys

PATTERNS = [
    r'\bTys\b', r'tvongaza', r'\btnas\b', r'\bNAS\b', r'192\.168\.', r'\bfd7a',
    r'tailscale', r'Moonlight', r'gaming-pc', r'\bgaza\b',
    r'\bClaude\b', r'\bCodex\b', r'\bOpus\b',
    r'/Users/', r'[A-Z]:\\\\', r'AppData', r'private-station',
]

# Anything after "base64," up to the closing quote is image data, not text.
BASE64 = re.compile(r'base64,[A-Za-z0-9+/=\s]+')

# The commit attribution trailer is deliberate and already on the open pull
# requests. Stripping it keeps the check from crying wolf on every commit,
# which is how a check stops being read.
TRAILER = re.compile(r'^Co-Authored-By: .*$', re.MULTILINE)


def check(path):
    with open(path, errors='replace') as fh:
        text = fh.read()
    stripped = TRAILER.sub('', BASE64.sub('base64,<image data>', text))

    findings = []
    for pattern in PATTERNS:
        for match in re.finditer(pattern, stripped):
            line = stripped.count('\n', 0, match.start()) + 1
            context = stripped[max(0, match.start() - 40):match.end() + 40].replace('\n', ' ')
            findings.append((line, match.group(0), context))
    return findings


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)

    total = 0
    for path in sys.argv[1:]:
        findings = check(path)
        total += len(findings)
        if findings:
            print(f'{path}: {len(findings)} finding(s)')
            for line, hit, context in findings[:10]:
                print(f'  line {line}: {hit}  ...{context}...')
        else:
            print(f'{path}: clean')

    if total:
        print(f'\n{total} finding(s): do not publish these as they are.')
    sys.exit(1 if total else 0)


if __name__ == '__main__':
    main()
