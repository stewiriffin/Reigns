/// <summary>
/// Checklist for low-end Android UI performance (pair with CanvasSplitHelper + Editor menus).
/// Open via: Reigns → Mobile UI → Print Canvas Split Instructions
/// </summary>
public static class MobileUiOptimizationGuide
{
    /*
     CANVAS SPLIT
     ------------
     Problem: Any dirty Graphic rebuilds the WHOLE Canvas mesh.
     Fix: StaticCanvas (backgrounds, static text) + DynamicCanvas (card, sliders, buttons).

     Menu: Reigns/Mobile UI/Split Selected Canvas Into Static + Dynamic
     Component: CanvasSplitHelper on DynamicCanvas

     Rules:
     - Identical CanvasScaler on both (e.g. 1080×1920, Match 0.5)
     - StaticCanvas.sortingOrder = 0, remove GraphicRaycaster
     - DynamicCanvas.sortingOrder = 1, keep GraphicRaycaster
     - pixelPerfect = false on mobile

     RAYCAST TARGETS
     ----------------
     Menu: Reigns/Mobile UI/Disable Unused Raycast Targets In Scene
     Disables raycastTarget on Text/Image/TMP that are not Buttons (or other Selectables).
     Cuts EventSystem raycast work every tap/frame.

     SPRITE ATLAS
     ------------
     Menu: Reigns/Mobile UI/Create / Refresh Card Sprite Atlas
     Creates Assets/Atlases/CardPortraits.spriteatlas and packs Character/portrait sprites.
     Put portraits under Assets/Resources/Characters (or Assets/Characters) then refresh.
     Goal: one atlas texture → far fewer UI draw calls for card faces.

     ALSO
     ----
     - MobileOptimizer @ 30 FPS
     - Avoid LayoutGroup rebuilds every frame on DynamicCanvas
     - Don't put Screen Space Overlay canvases you don't need (each is a separate sort)
    */
}
