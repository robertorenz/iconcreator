namespace IconCreator.Localization;

public enum AppLanguage { English, Spanish }

/// <summary>
/// Tiny in-memory localisation store. <see cref="T"/> looks up a key for the
/// current <see cref="Language"/>, falling back to English then to the key itself.
/// UI code subscribes to <see cref="Changed"/> to re-apply strings at runtime.
/// </summary>
public static class Loc
{
    private static AppLanguage _language = AppLanguage.English;

    public static AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;

    public static string T(string key)
    {
        var table = _language == AppLanguage.Spanish ? Spanish : English;
        if (table.TryGetValue(key, out var s)) return s;
        if (English.TryGetValue(key, out var e)) return e;
        return key;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    public static IEnumerable<AppLanguage> All => new[] { AppLanguage.English, AppLanguage.Spanish };

    public static string DisplayName(AppLanguage lang) =>
        lang == AppLanguage.Spanish ? "Español" : "English";

    // ============================ Tables ============================

    private static readonly Dictionary<string, string> English = new()
    {
        // Toolbar
        ["new"] = "New",
        ["open"] = "Open",
        ["recent"] = "Recent ▾",
        ["save"] = "Save",
        ["saveAs"] = "Save As",
        ["import"] = "Import Image",
        ["exportPng"] = "Export PNG",
        ["undo"] = "Undo",
        ["redo"] = "Redo",
        ["clear"] = "Clear",
        ["exportIco"] = "Export .ICO",
        ["language"] = "Language",
        ["vector"] = "Vector",
        ["newSvg"] = "New SVG",
        ["vectorTip"] = "New vector drawing — author SVG",

        // Vector editor
        ["vec.title"] = "Vector Editor — IconCreator Studio",
        ["vec.select"] = "Select",
        ["vec.path"] = "Path",
        ["vec.text"] = "Text",
        ["vec.fill"] = "FILL",
        ["vec.stroke"] = "STROKE",
        ["vec.strokeWidth"] = "STROKE WIDTH",
        ["vec.fontSize"] = "FONT SIZE",
        ["vec.none"] = "None",
        ["vec.delete"] = "Delete",
        ["vec.clearAll"] = "Clear all",
        ["vec.saveSvg"] = "Save SVG",
        ["vec.exportPng"] = "Export PNG",
        ["vec.shapes"] = "SHAPES",
        ["vec.hint.select"] = "Click a shape to select · drag to move · handles to resize · Delete to remove",
        ["vec.hint.draw"] = "Drag on the canvas to draw",
        ["vec.hint.path"] = "Click to add points · double-click or Enter to finish",
        ["vec.hint.text"] = "Click to place text, then type",
        ["vec.savedSvg"] = "Saved {0}",
        ["err.saveSvgTitle"] = "Could not save SVG",

        // Right panel
        ["colour"] = "COLOUR",
        ["chooseColour"] = "Choose colour…",
        ["brushSize"] = "BRUSH SIZE",
        ["fillTolerance"] = "FILL TOLERANCE",
        ["alphaBlend"] = "Alpha blend (respect opacity)",
        ["showGrid"] = "Show pixel grid",
        ["zoom"] = "ZOOM",
        ["fitWindow"] = "Fit to window",
        ["resolutions"] = "RESOLUTIONS",
        ["exportTick"] = "✓ = export",

        // Import bar
        ["placingImage"] = "Placing image",
        ["fit"] = "Fit",
        ["center"] = "Center",
        ["reset"] = "Reset",
        ["allSizes"] = "All sizes",
        ["allSizesTip"] = "Apply the placed image to every resolution, not just this one",
        ["cancel"] = "Cancel",
        ["apply"] = "Apply",

        // Tools
        ["tool.pencil"] = "Pencil",
        ["tool.eraser"] = "Eraser",
        ["tool.fill"] = "Flood Fill",
        ["tool.eyedropper"] = "Colour Picker",
        ["tool.line"] = "Line",
        ["tool.rect"] = "Rectangle",
        ["tool.rectFilled"] = "Filled Rectangle",
        ["tool.ellipse"] = "Ellipse",
        ["tool.ellipseFilled"] = "Filled Ellipse",

        // Status / hints
        ["ready"] = "Ready",
        ["hint.eyedropper"] = "Click a pixel to sample its colour",
        ["hint.fill"] = "Click to flood-fill a region",
        ["hint.shape"] = "Drag to draw the shape",
        ["hint.placing"] = "Placing {0} — drag to move, corners to resize (Shift = keep proportions), then Apply",
        ["hint.dropPlace"] = "Drop to place image  (hold Ctrl to open as a new tab)",
        ["hint.dropOpen"] = "Drop to open as a new tab",
        ["msg.saved"] = "Saved {0}",
        ["msg.opened"] = "Opened {0}",
        ["msg.exported"] = "Exported {0}",
        ["msg.importCancelled"] = "Import cancelled",
        ["msg.applied"] = "Image applied",
        ["msg.appliedAll"] = "Image applied to all sizes",

        // Recent
        ["recentIcons"] = "RECENT ICONS",
        ["noRecent"] = "No recent files yet.",
        ["clearList"] = "Clear list",

        // Dialogs — generic
        ["ok"] = "OK",
        ["create"] = "Create",
        ["select"] = "Select",
        ["discard"] = "Discard",
        ["close"] = "Close",
        ["untitled"] = "Untitled",

        // Discard / close
        ["discardTitle"] = "Discard changes?",
        ["discardMsg"] = "This icon has unsaved changes. Continue and lose them?",
        ["closeOne"] = "One open icon has unsaved changes. Close anyway?",
        ["closeMany"] = "{0} open icons have unsaved changes. Close anyway?",

        // New icon dialog
        ["newIcon.title"] = "New Icon",
        ["newIcon.include"] = "Include resolutions",
        ["newIcon.noSizesTitle"] = "No sizes selected",
        ["newIcon.noSizesMsg"] = "Choose at least one resolution.",

        // Colour dialog
        ["color.title"] = "Select Colour",
        ["color.hex"] = "Hex",
        ["color.transparent"] = "Transparent",

        // Errors
        ["err.openTitle"] = "Could not open file",
        ["err.saveTitle"] = "Could not save icon",
        ["err.importTitle"] = "Could not import image",
        ["err.exportTitle"] = "Could not export PNG",
        ["err.unexpectedTitle"] = "Unexpected error",
        ["err.noImages"] = "No images found in file.",
        ["err.noImageData"] = "No image data found.",
        ["err.noSizesExport"] = "No sizes selected for export.",
        ["err.fileMissing"] = "The file no longer exists.",
    };

    private static readonly Dictionary<string, string> Spanish = new()
    {
        // Toolbar
        ["new"] = "Nuevo",
        ["open"] = "Abrir",
        ["recent"] = "Recientes ▾",
        ["save"] = "Guardar",
        ["saveAs"] = "Guardar como",
        ["import"] = "Importar imagen",
        ["exportPng"] = "Exportar PNG",
        ["undo"] = "Deshacer",
        ["redo"] = "Rehacer",
        ["clear"] = "Borrar",
        ["exportIco"] = "Exportar .ICO",
        ["language"] = "Idioma",
        ["vector"] = "Vector",
        ["newSvg"] = "Nuevo SVG",
        ["vectorTip"] = "Nuevo dibujo vectorial — crear SVG",

        // Vector editor
        ["vec.title"] = "Editor vectorial — IconCreator Studio",
        ["vec.select"] = "Seleccionar",
        ["vec.path"] = "Trazado",
        ["vec.text"] = "Texto",
        ["vec.fill"] = "RELLENO",
        ["vec.stroke"] = "CONTORNO",
        ["vec.strokeWidth"] = "GROSOR DEL CONTORNO",
        ["vec.fontSize"] = "TAMAÑO DE FUENTE",
        ["vec.none"] = "Ninguno",
        ["vec.delete"] = "Eliminar",
        ["vec.clearAll"] = "Borrar todo",
        ["vec.saveSvg"] = "Guardar SVG",
        ["vec.exportPng"] = "Exportar PNG",
        ["vec.shapes"] = "FORMAS",
        ["vec.hint.select"] = "Haz clic en una forma para seleccionar · arrastra para mover · tiradores para redimensionar · Supr para eliminar",
        ["vec.hint.draw"] = "Arrastra en el lienzo para dibujar",
        ["vec.hint.path"] = "Haz clic para añadir puntos · doble clic o Enter para terminar",
        ["vec.hint.text"] = "Haz clic para colocar texto y luego escribe",
        ["vec.savedSvg"] = "Guardado {0}",
        ["err.saveSvgTitle"] = "No se pudo guardar el SVG",

        // Right panel
        ["colour"] = "COLOR",
        ["chooseColour"] = "Elegir color…",
        ["brushSize"] = "TAMAÑO DE PINCEL",
        ["fillTolerance"] = "TOLERANCIA DE RELLENO",
        ["alphaBlend"] = "Mezcla alfa (respetar opacidad)",
        ["showGrid"] = "Mostrar cuadrícula de píxeles",
        ["zoom"] = "ZOOM",
        ["fitWindow"] = "Ajustar a la ventana",
        ["resolutions"] = "RESOLUCIONES",
        ["exportTick"] = "✓ = exportar",

        // Import bar
        ["placingImage"] = "Colocando imagen",
        ["fit"] = "Ajustar",
        ["center"] = "Centrar",
        ["reset"] = "Restablecer",
        ["allSizes"] = "Todos los tamaños",
        ["allSizesTip"] = "Aplicar la imagen a todas las resoluciones, no solo a esta",
        ["cancel"] = "Cancelar",
        ["apply"] = "Aplicar",

        // Tools
        ["tool.pencil"] = "Lápiz",
        ["tool.eraser"] = "Borrador",
        ["tool.fill"] = "Relleno",
        ["tool.eyedropper"] = "Cuentagotas",
        ["tool.line"] = "Línea",
        ["tool.rect"] = "Rectángulo",
        ["tool.rectFilled"] = "Rectángulo relleno",
        ["tool.ellipse"] = "Elipse",
        ["tool.ellipseFilled"] = "Elipse rellena",

        // Status / hints
        ["ready"] = "Listo",
        ["hint.eyedropper"] = "Haz clic en un píxel para tomar su color",
        ["hint.fill"] = "Haz clic para rellenar una región",
        ["hint.shape"] = "Arrastra para dibujar la forma",
        ["hint.placing"] = "Colocando {0} — arrastra para mover, esquinas para redimensionar (Mayús = mantener proporciones), luego Aplicar",
        ["hint.dropPlace"] = "Suelta para colocar la imagen  (mantén Ctrl para abrir en una pestaña nueva)",
        ["hint.dropOpen"] = "Suelta para abrir en una pestaña nueva",
        ["msg.saved"] = "Guardado {0}",
        ["msg.opened"] = "Abierto {0}",
        ["msg.exported"] = "Exportado {0}",
        ["msg.importCancelled"] = "Importación cancelada",
        ["msg.applied"] = "Imagen aplicada",
        ["msg.appliedAll"] = "Imagen aplicada a todos los tamaños",

        // Recent
        ["recentIcons"] = "ICONOS RECIENTES",
        ["noRecent"] = "Aún no hay archivos recientes.",
        ["clearList"] = "Borrar lista",

        // Dialogs — generic
        ["ok"] = "Aceptar",
        ["create"] = "Crear",
        ["select"] = "Seleccionar",
        ["discard"] = "Descartar",
        ["close"] = "Cerrar",
        ["untitled"] = "Sin título",

        // Discard / close
        ["discardTitle"] = "¿Descartar cambios?",
        ["discardMsg"] = "Este icono tiene cambios sin guardar. ¿Continuar y perderlos?",
        ["closeOne"] = "Un icono abierto tiene cambios sin guardar. ¿Cerrar de todos modos?",
        ["closeMany"] = "{0} iconos abiertos tienen cambios sin guardar. ¿Cerrar de todos modos?",

        // New icon dialog
        ["newIcon.title"] = "Nuevo icono",
        ["newIcon.include"] = "Incluir resoluciones",
        ["newIcon.noSizesTitle"] = "No se seleccionaron tamaños",
        ["newIcon.noSizesMsg"] = "Elige al menos una resolución.",

        // Colour dialog
        ["color.title"] = "Seleccionar color",
        ["color.hex"] = "Hex",
        ["color.transparent"] = "Transparente",

        // Errors
        ["err.openTitle"] = "No se pudo abrir el archivo",
        ["err.saveTitle"] = "No se pudo guardar el icono",
        ["err.importTitle"] = "No se pudo importar la imagen",
        ["err.exportTitle"] = "No se pudo exportar el PNG",
        ["err.unexpectedTitle"] = "Error inesperado",
        ["err.noImages"] = "No se encontraron imágenes en el archivo.",
        ["err.noImageData"] = "No se encontraron datos de imagen.",
        ["err.noSizesExport"] = "No hay tamaños seleccionados para exportar.",
        ["err.fileMissing"] = "El archivo ya no existe.",
    };
}
