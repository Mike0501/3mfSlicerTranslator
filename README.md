# 3MF Translator

3MF Translator is a local Windows app that combines the geometry and compatible print settings from a 3MF project with the printer profile from a project saved in the destination slicer.

## Getting started

Open `bin/Release/esito/3MFTranslator.exe`. To build from source, install the .NET 10 SDK and run the build command.

```powershell
dotnet build -c Release -o bin/Release/esito
```

Use the language selector in the upper-right corner to switch between English and Italian. This also changes the language of the report and result details. The sun and moon switch changes the theme in the current window, and the question mark opens the quick guide. The app saves your language and theme preferences.

## Converting a project

1. Click **Browse…** and choose the source 3MF. Under **Source settings to import**, use the active settings or choose an embedded process preset if one is available. The app uses the active settings for any fields missing from the selected preset.
2. Under **Destination**, click **Reference 3MF…** and choose a project saved in the destination slicer with the printer and presets you want to use. To reuse a saved profile, click **Profile…**. Then click **Translate and save…** and choose a new file name.
3. If the print settings differ, choose **Use source settings**, **Keep destination settings**, or **Review manually…**. In the manual review, you can filter settings by name and choose values individually. The app keeps the destination value for any setting without a compatible source value. Click **Cancel** to stop the conversion.
4. Click **Result details…** below the report. The button is red for errors, yellow for warnings without errors, and green for a successful conversion without warnings. Open the translated 3MF **as a project** in the destination slicer, slice it, and check the preview before printing.

Bambu Studio, OrcaSlicer, and Anycubic Slicer Next are supported as both sources and destinations. The app also imports projects from PrusaSlicer in its 2.x format and from SuperSlicer when they contain geometry and embedded settings. The destination requires an FFF nozzle. Bambu or Orca projects with multiple nozzles can be converted if all source nozzles match the diameter of the destination nozzle.

If the source project has more filament slots than the destination, the app removes unused slots when it can verify the assignments. The report shows how source slots map to destination slots. Projects with painted colors or per-layer sequences need enough destination slots to preserve the original indices.

PrusaSlicer and SuperSlicer imports support projects with one build plate and one filament, including negative volumes, modifiers, instances, and compatible process settings. The report flags variable layer heights, per-layer events, and height ranges from these formats as excluded. Output projects use the Bambu/Orca format. This version excludes Cura, the new PrusaSlicer 3 project format, and virtual filaments.

### Destination profiles

Click **Create profile… → Add from 3MF…** to save a reference from any supported destination slicer. You can do this before opening a source project. The library stores a copy of the 3MF under `%LOCALAPPDATA%\3MFTranslator\references`. Use **Profile…** to select it for a conversion. You can create and reuse these profiles on a PC without the slicer that generated the reference.

Each reference serves as a base preset for its printer. Select it in the library and click **Customize…** to save a variant with your own layer height, wall count, sparse infill density, and support settings. Variants share the reference file and can be edited later. Choose **Keep destination settings** during conversion to apply the selected variant, or use the manual review to choose individual settings.

References added in an earlier version continue to use their saved paths. You can migrate older local presets with **Convert to 3MF…** or when you first use them. Migration requires the original slicer with the presets still available. Subsequent conversions use the reference saved in the library.

The output preserves meshes, build plates, object positions, colors, and slot assignments in the shared format. The app creates an embedded process preset with compatible source settings, including layer height, wall count, infill, supports, print speeds, and accelerations. The report lists the base layer height, where the settings came from, and any detected customizations that could not be transferred. The printer configuration, machine limits, temperatures, filament profiles, and purging settings come from the reference project. If the build plate size changes, check that the model fits in the destination slicer. The app removes the source G-code and reports custom height-range settings as excluded.

## Command-line usage

```powershell
dotnet bin/Release/esito/3MFTranslator.dll convert source.3mf reference.3mf translated.3mf report.txt
```

Use new paths for the output project and report. The command exits with code `0` when the project is saved, `1` for a conversion or write error, or `2` for invalid arguments.

To select an embedded preset, add its path within the archive after the report path, for example `Metadata/process_settings_1.config`.
