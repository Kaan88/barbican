"""Extract Bulwark's settings pages into desktop/ui/settings-schema.json.

Reads components/settings/*.tsx (SettingsSection / SettingItem / ToggleSwitch / Select /
RadioGroup + updateSetting('key')) and resolves labels through locales/en/common.json.
Run from the repo root:  python desktop/tools/extract_settings.py
"""
import json, re, os, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'
EN = json.load(open(os.path.join(ROOT, 'locales/en/common.json'), encoding='utf8'))

TABS = [
    # id, label, group, icon (Segoe Fluent), component files
    ('account', 'Account', 'top', '', ['account-settings', 'your-name-row', 'identity-settings']),
    ('security', 'Password & sign-in', 'top', '', ['account-security-settings', 'app-lock-settings']),
    ('privacy', 'Privacy & security', 'top', '', ['privacy-settings']),
    ('notifications', 'Notifications', 'top', '', ['notification-settings']),
    ('general', 'General', 'top', '', ['general-settings', 'language-settings', 'protocol-handler-settings']),
    ('appearance', 'Appearance', 'top', '', ['appearance-settings', 'themes-settings', 'layout-settings', 'sidebar-apps-settings']),
    ('accessibility', 'Accessibility', 'top', '', ['accessibility-settings']),
    ('message_list', 'Message list', 'mail', '', ['message-list-settings', 'message-list-order-settings']),
    ('reading', 'Reading', 'mail', '', ['reading-settings']),
    ('composing', 'Composing', 'mail', '', ['composing-settings', 'signatures-section', 'template-settings', 'proofreading-settings']),
    ('organize', 'Folders & tags', 'mail', '', ['organize-settings', 'folder-settings', 'keyword-settings']),
    ('filters', 'Filters', 'mail', '', ['filter-settings']),
    ('vacation', 'Out of office', 'mail', '', ['vacation-settings']),
    ('recover', 'Recover deleted items', 'mail', '', ['recover-deleted-settings']),
    ('calendar', 'Calendar', 'apps', '', ['calendar-settings', 'calendar-management-settings']),
    ('contacts', 'Contacts', 'apps', '', ['contacts-settings', 'address-book-management-settings']),
    ('files', 'Files', 'apps', '', ['files-settings', 'downloads-settings']),
    ('addons', 'Add-ons', 'apps', '', ['addons-settings', 'plugins-settings']),
    ('backup', 'Backup & import', 'bottom', '', []),
    ('troubleshooting', 'Troubleshooting', 'bottom', '', ['debug-settings']),
    ('about', 'About', 'bottom', '', ['about-data-settings']),
]


def lookup(ns, key):
    node = EN
    for part in (ns + '.' + key).split('.'):
        if not isinstance(node, dict) or part not in node:
            return None
        node = node[part]
    return node if isinstance(node, str) else None


def defaults():
    src = open(os.path.join(ROOT, 'stores/settings-store.ts'), encoding='utf8').read()
    body = src[src.index('const DEFAULT_SETTINGS = {'):]
    body = body[:body.index('\n};')]
    out = {}
    for m in re.finditer(r"^\s+(\w+):\s*(true|false|-?\d+(?:\.\d+)?|'[^']*')", body, re.M):
        v = m.group(2)
        out[m.group(1)] = True if v == 'true' else False if v == 'false' else v.strip("'") if v.startswith("'") else float(v) if '.' in v else int(v)
    return out


T_CALL = r"(\w+)\(\s*['\"`]([\w.]+)['\"`]"


def extract(path):
    src = open(path, encoding='utf8').read()
    tr = {m.group(1): m.group(2) for m in re.finditer(r"const (\w+) = useTranslations\(\s*['\"]([\w.]+)['\"]\s*\)", src)}

    def res(expr):
        if not expr:
            return None
        m = re.search(T_CALL, expr)
        if not m or m.group(1) not in tr:
            return None
        return lookup(tr[m.group(1)], m.group(2))

    rows = []
    # tokens in document order
    pat = re.compile(r"<SettingsSection\b(?P<sec>[^>]*?)>|<SettingItem\b(?P<item>.*?)</SettingItem>", re.S)
    for m in pat.finditer(src):
        if m.group('sec') is not None:
            t = res(re.search(r"title=\{([^}]*)\}", m.group('sec')).group(1)) if re.search(r"title=\{([^}]*)\}", m.group('sec')) else None
            d = re.search(r"description=\{([^}]*)\}", m.group('sec'))
            if t:
                rows.append({'kind': 'section', 'label': t, 'desc': res(d.group(1)) if d else ''})
            continue
        block = m.group('item')
        head = block.split('>', 1)[0] if '>' in block else block
        lab = re.search(r"label=\{([^}]*)\}", block)
        des = re.search(r"description=\{([^{}]*(?:\{[^}]*\}[^{}]*)*)\}", head)
        label = res(lab.group(1)) if lab else None
        if not label:
            continue
        desc = res(des.group(1)) if des else ''
        key = re.search(r"updateSetting\(\s*['\"](\w+)['\"]", block)
        neg = bool(key and re.search(r"updateSetting\(\s*['\"]\w+['\"]\s*,\s*!", block))
        row = {'kind': 'info', 'label': label, 'desc': desc or '', 'key': key.group(1) if key else ''}
        if 'ToggleSwitch' in block or "type=\"checkbox\"" in block:
            row['kind'] = 'toggle'
            if neg:
                row['invert'] = True
        elif '<Select' in block or '<RadioGroup' in block or 'SegmentedControl' in block or '<select' in block:
            row['kind'] = 'select'
            opts = []
            for om in re.finditer(r"\{\s*value:\s*['\"]?([\w.-]+)['\"]?\s*(?:as \w+\s*)?,\s*label:\s*([^}]+?)\s*\}", block):
                lbl = res(om.group(2)) or om.group(2).strip("'\" ")
                opts.append([om.group(1), lbl])
            row['options'] = opts
            if not opts:
                # options defined elsewhere in the file (e.g. const FOO_OPTIONS = [...])
                ref = re.search(r"options=\{(\w+)", block)
                if ref:
                    dm = re.search(r"const " + ref.group(1) + r"\b[^=]*=\s*\[(.*?)\];", src, re.S)
                    if dm:
                        for om in re.finditer(r"\{\s*value:\s*['\"]?([\w.-]+)['\"]?\s*(?:as \w+\s*)?,\s*label:\s*([^}]+?)\s*\}", dm.group(1)):
                            opts.append([om.group(1), res(om.group(2)) or om.group(2).strip("'\" ")])
        elif '<Input' in block or '<input' in block or '<Textarea' in block or '<textarea' in block:
            row['kind'] = 'text'
        rows.append(row)
    return rows


def main():
    dflt = defaults()
    out = {'tabs': []}
    for tid, label, group, icon, files in TABS:
        rows = []
        for f in files:
            p = os.path.join(ROOT, 'components/settings', f + '.tsx')
            if os.path.exists(p):
                rows += extract(p)
        seen, uniq = set(), []
        for r in rows:
            k = (r['kind'], r['label'])
            if k in seen:
                continue
            seen.add(k)
            if r.get('key') in dflt:
                r['default'] = dflt[r['key']]
            uniq.append(r)
        out['tabs'].append({'id': tid, 'label': label, 'group': group, 'icon': icon, 'rows': uniq})
    dst = os.path.join(ROOT, 'desktop/ui/settings-schema.json')
    json.dump(out, open(dst, 'w', encoding='utf8'), ensure_ascii=False, indent=1)
    n = sum(len(t['rows']) for t in out['tabs'])
    k = sum(1 for t in out['tabs'] for r in t['rows'] if r.get('key'))
    print(f"{n} rows, {k} bound to settings keys")
    for t in out['tabs']:
        print(f"  {t['label']}: {len(t['rows'])}")


main()
