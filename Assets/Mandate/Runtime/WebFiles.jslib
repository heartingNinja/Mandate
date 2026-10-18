// WebFiles.jslib — the two browser calls WebFiles.cs needs in a WebGL build. Unity includes it automatically.

mergeInto(LibraryManager.library, {

  // Files under Application.persistentDataPath live in the browser's IndexedDB. Writing one only changes the copy
  // in memory; this copies it into IndexedDB so it is still there next visit.
  MandateSaveFiles: function () {
    if (typeof FS === "undefined" || !FS.syncfs) return;
    FS.syncfs(false, function (err) { if (err) console.warn("Mandate: could not save files: " + err); });
  },

  // Offers some text to the player as a downloaded file.
  MandateDownloadText: function (namePtr, textPtr) {
    var name = UTF8ToString(namePtr);
    var text = UTF8ToString(textPtr);
    var url = URL.createObjectURL(new Blob([text], { type: "text/plain;charset=utf-8" }));
    var link = document.createElement("a");
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
  },
});
