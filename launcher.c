// launcher.c - Onsay.exe in the folder Onsay is installed in, the one file his shortcuts, startup and the uninstaller
// open. It starts the version version.txt names, app-<version>\Onsay.exe, with the same arguments, and waits for it, so
// an update is only version.txt changing (src/System/Updates.cs) and never touches a file in use. Built by release.ps1
// for 32-bit Windows, which runs on every Windows from 7 on, with launcher.rc giving it his icon.
#include <windows.h>

int WINAPI WinMain(HINSTANCE instance, HINSTANCE previous, LPSTR ignored, int show) {
  wchar_t here[MAX_PATH], app[MAX_PATH + 64], folder[MAX_PATH + 64], version[24] = L"";
  char text[24] = "";
  DWORD got = 0;
  STARTUPINFOW start = { sizeof start };
  PROCESS_INFORMATION run;
  (void)instance, (void)previous, (void)ignored, (void)show;

  GetModuleFileNameW(NULL, here, MAX_PATH);
  *wcsrchr(here, L'\\') = 0;
  wsprintfW(app, L"%s\\version.txt", here);
  HANDLE f = CreateFileW(app, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
  if (f != INVALID_HANDLE_VALUE) {
    ReadFile(f, text, sizeof text - 1, &got, NULL);
    CloseHandle(f);
  }
  // A version is digits and dots, read up to anything else, like the line's end.
  for (DWORD i = 0; i < got && ((text[i] >= '0' && text[i] <= '9') || text[i] == '.'); i++) version[i] = (wchar_t)text[i];
  wsprintfW(folder, L"%s\\app-%s", here, version);
  wsprintfW(app, L"%s\\Onsay.exe", folder);

  // Its own command line with the program's own name taken off, which comes first, quoted or not.
  wchar_t *args = GetCommandLineW();
  wchar_t end = *args == L'"' ? L'"' : L' ';
  for (args += *args == L'"'; *args && *args != end; args++) {}
  if (*args) args++;
  wchar_t *line = HeapAlloc(GetProcessHeap(), 0, (lstrlenW(app) + lstrlenW(args) + 4) * sizeof(wchar_t));
  wsprintfW(line, L"\"%s\" %s", app, args);

  if (!*version || !CreateProcessW(app, line, NULL, NULL, FALSE, 0, NULL, folder, &start, &run)) {
    MessageBoxW(NULL, L"Onsay couldn't start, because some of his files are missing. Install him again from onsay.dev.",
        L"Onsay", MB_ICONERROR);
    return 1;
  }
  DWORD code = 0;
  WaitForSingleObject(run.hProcess, INFINITE);
  GetExitCodeProcess(run.hProcess, &code);
  return (int)code;
}
