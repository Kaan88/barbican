#Requires -Version 5.1
<#
  S5: document the Rust app's Windows Credential Manager entries for the migration, READ-ONLY.
  CredEnumerateW only (no write, delete or modify API is imported). Prints names, type, persistence, blob size,
  encoding and JSON STRUCTURE (key names, array length). It never prints a blob's content or any value:
  the one value shown is the non-secret "kind" discriminator, and only if it is exactly oauth or password.
#>
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CredProbe {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  struct CREDENTIAL {
    public uint Flags, Type; public string TargetName, Comment; public long LastWritten;
    public uint BlobSize; public IntPtr Blob; public uint Persist, AttrCount; public IntPtr Attrs;
    public string TargetAlias, UserName;
  }
  [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern bool CredEnumerateW(string filter, uint flags, out uint count, out IntPtr creds);
  [DllImport("advapi32.dll")] static extern void CredFree(IntPtr p);

  public static List<string[]> List(string filter) {
    var rows = new List<string[]>(); uint n; IntPtr arr;
    if (!CredEnumerateW(filter, 0, out n, out arr)) return rows;
    for (int i = 0; i < n; i++) {
      var c = (CREDENTIAL)Marshal.PtrToStructure(Marshal.ReadIntPtr(arr, i * IntPtr.Size), typeof(CREDENTIAL));
      byte[] b = new byte[c.BlobSize];
      if (c.BlobSize > 0) Marshal.Copy(c.Blob, b, 0, b.Length);
      bool utf16 = b.Length % 2 == 0;
      string shape = utf16 ? Shape(Encoding.Unicode.GetString(b)) : "n/a";
      Array.Clear(b, 0, b.Length);
      rows.Add(new string[] { c.TargetName, c.Type.ToString(), c.Persist.ToString(), c.UserName, c.Comment,
                              c.BlobSize.ToString(), utf16 ? "utf-16le" : "not-utf16", shape });
    }
    CredFree(arr);
    return rows;
  }

  static string Shape(string t) {
    t = t.Trim();
    if (t.StartsWith("{")) return "json-object keys=" + Keys(t, 0);
    if (t.StartsWith("[")) return "json-array " + Keys(t, 1);
    return "plain-text length=" + t.Length;
  }

  // Key names at object depth only (never values); for arrays, the element count and the keys of the elements.
  static string Keys(string t, int arr) {
    var keys = new List<string>(); int depth = 0, count = 0; bool inStr = false; var sb = new StringBuilder();
    for (int i = 0; i < t.Length; i++) {
      char ch = t[i];
      if (inStr) {
        if (ch == (char)92) { i++; continue; }
        if (ch == (char)34) {
          inStr = false;
          int j = i + 1; while (j < t.Length && char.IsWhiteSpace(t[j])) j++;
          if (j < t.Length && t[j] == ':' && depth == 1 + arr) { string k = sb.ToString(); if (!keys.Contains(k)) keys.Add(k); }
          sb.Clear();
        } else sb.Append(ch);
        continue;
      }
      if (ch == (char)34) { inStr = true; continue; }
      if (ch == '{' || ch == '[') { depth++; if (arr == 1 && ch == '{' && depth == 2) count++; }
      else if (ch == '}' || ch == ']') depth--;
    }
    string kind = "";
    int p = t.IndexOf("\"kind\":\"oauth\"", StringComparison.Ordinal); if (p >= 0) kind = " first-kind=oauth";
    else { p = t.IndexOf("\"kind\":\"password\"", StringComparison.Ordinal); if (p >= 0) kind = " first-kind=password"; }
    return (arr == 1 ? "elements=" + count + " element-keys=" : "") + string.Join(",", keys) + kind;
  }
}
'@

# Only the four known Barbican target names are enumerated (CredEnumerate takes a prefix pattern and
# "*Barbican*" matches nothing), so no unrelated credential is ever loaded.
$names = 'session.Barbican', 'accounts.Barbican', 'stalwart-admin-key.Barbican', 'ai-api-key.Barbican'
foreach ($n in $names) {
    foreach ($r in [CredProbe]::List($n)) {
        '{0}' -f $r[0]
        '  type={0} persist={1} username={2} comment={3}' -f $r[1], $r[2], $r[3], $r[4]
        '  blob={0} bytes, {1}, shape: {2}' -f $r[5], $r[6], $r[7]
    }
}
