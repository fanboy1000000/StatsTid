// EVIDENCE ONLY (draft PR, never merged): each line below exercises one branch of tools/check_docs.py check 5.
// Shortcut(QUAL-069): probe 1, wrong case
/// shortcut(pending): probe 7, pending
// shortcut(QUAL-999): probe 8, no such QUAL row
// shortcut(QUAL-BF-002): probe 12, its appendix row names another file
// shortcut(QUAL-BF-003): probe 13, its appendix row uses the wrong path style
var s = "// shortcut(pending): probe 14, a string literal (documented false positive)";
/// shortcut(QUAL-069): probe 15, valid
