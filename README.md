# SystemWatch

Ein übersichtlicher Ersatz für den Windows-Task-Manager: Live-Graphen für CPU, Arbeitsspeicher,
GPU, Netzwerk und Datenträger sowie eine Prozessliste, gruppiert nach Apps, Hintergrund- und
Windows-Prozessen. Standardmäßig dunkel, auf Wunsch hell oder passend zu Windows
(Einstellungen → Darstellung → Design).

## Als Task-Manager verwenden

Unter **Einstellungen → System → Task-Manager ersetzen** öffnen Strg+Umschalt+Esc, „Task-Manager“
im Kontextmenü der Taskleiste und Strg+Alt+Entf → Task-Manager künftig SystemWatch. Läuft
SystemWatch schon, kommt einfach das vorhandene Fenster nach vorn.

Technisch trägt SystemWatch sich – wie Process Explorer – unter
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe`
als `Debugger` ein. Deshalb fragt Windows beim Umschalten nach Administratorrechten.

Wichtig:

- Windows startet genau die Datei, die beim Einschalten verwendet wurde. SystemWatch vorher an
  einen festen Ort legen, am besten `C:\Program Files\SystemWatch\` (dort kann nur ein Administrator
  die Exe austauschen). Vor dem Verschieben oder Löschen die Option wieder ausschalten.
- Wurde die Datei trotzdem verschoben, zeigt SystemWatch das in den Einstellungen an und bietet an,
  den alten Eintrag zu entfernen. Ohne SystemWatch geht das in einer Administrator-Eingabeaufforderung:

  ```powershell
  reg delete "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe" /v Debugger /f
  ```

## Download

Die aktuelle Version gibt es unter [Releases](../../releases/latest):
`SystemWatch-<version>.zip` herunterladen, entpacken, `SystemWatch.exe` starten.
Voraussetzung ist Windows 10 oder 11 (das mitgelieferte .NET Framework 4.8 genügt).

### Signatur prüfen

Die Exe ist mit einem **selbstsignierten** Zertifikat signiert. Windows kennt es nicht und zeigt
deshalb beim ersten Start eventuell „Unbekannter Herausgeber“ bzw. eine SmartScreen-Warnung.
Die Echtheit lässt sich trotzdem prüfen:

- **Fingerabdruck** der Signatur (Eigenschaften → Digitale Signaturen) muss lauten:
  `6E18FD7857AC75BDF2E227BEB3783B5B210768D1` – das öffentliche Zertifikat liegt unter
  [`signing/SystemWatch-CodeSigning.cer`](signing/SystemWatch-CodeSigning.cer).
- **Build-Nachweis** von GitHub (belegt, dass die Datei aus diesem Repo gebaut wurde):
  ```powershell
  gh attestation verify SystemWatch-<version>.zip --repo HitzlikeDna/SystemWatch
  ```

## Selbst bauen

Visual Studio 2022 oder neuer (Workload „.NET-Desktopentwicklung“):

```powershell
msbuild SystemWatch/SystemWatch.csproj /restore /p:Configuration=Release
```

## Neue Version veröffentlichen

Jeder Push und Pull Request wird automatisch gebaut (GitHub Actions, Ergebnis als Download am
Workflow-Lauf). Ein Release entsteht durch einen Versions-Tag:

```powershell
git tag v2.1.0
git push origin v2.1.0
```

Die Pipeline baut dann die Release-Version, signiert `SystemWatch.exe`, erstellt den
Build-Nachweis und veröffentlicht ZIP, Zertifikat und Prüfsummen als GitHub-Release.
Die Versionsnummer kommt aus dem Tag.

Für die Signierung braucht das Repo die Secrets `SIGNING_CERT_PFX_BASE64` und
`SIGNING_CERT_PASSWORD`. Fehlen sie, wird unsigniert gebaut.
