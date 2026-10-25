using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProceduralRoads;

/// <summary>One host-only loading panel, projected exclusively from the owner's detached snapshots.</summary>
internal static class RoadGenerationPanel
{
    private static GameObject? panel;
    private static Hud? hud;
    private static readonly List<TMP_Text> rows = new();
    private static RoadGenerationProgress? shown;
    private static Button? cancel;
    private static TMP_Text? cancelText;
    private static CanvasGroup? interaction;
    private static RectTransform? viewport, content;
    private static Vector2 layoutSize = new(-1,-1);
    internal static bool OwnsPointer => panel!=null && panel.activeInHierarchy && hud==Hud.instance
        && RoadLifecycleManager.InitialGenerationActive && !VanillaModal;
    private static bool VanillaModal => Menu.IsActive() || Menu.IsVisible() || UnifiedPopup.IsVisible()
        || TextInput.IsVisible() || ZNet.IsPasswordDialogShowing();
    internal static void Synchronize()
    {
        if(!RoadLifecycleManager.InitialGenerationActive || hud==null || hud!=Hud.instance) { Clear(); return; }
        Relayout();
        if(interaction!=null) { interaction.alpha=VanillaModal?0:1; interaction.interactable=!VanillaModal; interaction.blocksRaycasts=!VanillaModal; }
    }
    internal static void Clear()
    {
        if(panel!=null) UnityEngine.Object.Destroy(panel);
        panel=null; hud=null; shown=null; cancel=null; cancelText=null; interaction=null; viewport=null; content=null; layoutSize=new Vector2(-1,-1); rows.Clear();
    }
    internal static void Show(Hud owner, RoadGenerationProgress progress)
    {
        if(panel==null || hud!=owner || shown?.RunId!=progress.RunId || rows.Count!=Math.Max(1,progress.Islands.Count))
        {
            Clear();
            var indicator=owner.m_loadingIndicator;
            var canvas=indicator.GetComponentInParent<Canvas>();
            var template=indicator.GetComponentInChildren<TMP_Text>(true);
            if(canvas==null || template==null) return;
            hud=owner;
            panel=new GameObject("Road generation",typeof(RectTransform),typeof(Image),typeof(CanvasGroup),typeof(Canvas),typeof(GraphicRaycaster));
            panel.transform.SetParent(canvas.transform,false);
            interaction=panel.GetComponent<CanvasGroup>();
            interaction.ignoreParentGroups=true;
            panel.GetComponent<Canvas>().overrideSorting=false;
            var rect=(RectTransform)panel.transform;
            rect.anchorMin=new Vector2(.10f,.18f); rect.anchorMax=new Vector2(.90f,.78f);
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            var backing=panel.GetComponent<Image>(); backing.color=new Color(0,0,0,.72f); backing.raycastTarget=false;
            var viewObject=new GameObject("Activity viewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D));
            viewObject.transform.SetParent(panel.transform,false);
            var view=(RectTransform)viewObject.transform;
            viewport=view;
            view.anchorMin=Vector2.zero; view.anchorMax=Vector2.one; view.offsetMin=new Vector2(16,56); view.offsetMax=new Vector2(-16,-12);
            viewObject.GetComponent<Image>().color=Color.clear;
            var buttonObject=new GameObject("Cancel and return to menu",typeof(RectTransform),typeof(Image),typeof(Button));
            buttonObject.transform.SetParent(panel.transform,false);
            var buttonRect=(RectTransform)buttonObject.transform;
            buttonRect.anchorMin=new Vector2(.25f,0); buttonRect.anchorMax=new Vector2(.75f,0);
            buttonRect.pivot=new Vector2(.5f,0); buttonRect.anchoredPosition=new Vector2(0,10); buttonRect.sizeDelta=new Vector2(0,36);
            buttonObject.GetComponent<Image>().color=new Color(.25f,.25f,.25f,.95f);
            cancel=buttonObject.GetComponent<Button>();
            long expectedRun=progress.RunId;
            cancel.onClick.AddListener(()=> {
                if(RoadLifecycleManager.Progress.Phase=="ReturningToMenu" || RoadLifecycleManager.Progress.Phase=="MenuFailed")
                    RoadLifecycleManager.RetryReturnToMenu(expectedRun);
                else RoadLifecycleManager.RequestCancelToMenu(expectedRun);
            });
            var label=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI)); label.transform.SetParent(buttonRect,false);
            var labelRect=(RectTransform)label.transform; labelRect.anchorMin=Vector2.zero; labelRect.anchorMax=Vector2.one; labelRect.offsetMin=labelRect.offsetMax=Vector2.zero;
            cancelText=label.GetComponent<TextMeshProUGUI>(); cancelText.font=template.font; cancelText.fontSharedMaterial=template.fontSharedMaterial;
            cancelText.fontSize=18; cancelText.alignment=TextAlignmentOptions.Center; cancelText.raycastTarget=false;
            var contentObject=new GameObject("Island slots",typeof(RectTransform)); contentObject.transform.SetParent(view,false);
            var area=(RectTransform)contentObject.transform;
            content=area;
            area.anchorMin=new Vector2(0,1); area.anchorMax=Vector2.one; area.pivot=new Vector2(.5f,1);
            area.offsetMin=area.offsetMax=Vector2.zero;
            var scroll=panel.AddComponent<ScrollRect>(); scroll.viewport=view; scroll.content=area;
            scroll.horizontal=false; scroll.vertical=true; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=28;
            int count=Math.Max(1,progress.Islands.Count);
            for(int i=0;i<count;i++)
            {
                var item=new GameObject("Island slot "+(i+1),typeof(RectTransform),typeof(TextMeshProUGUI)); item.transform.SetParent(area,false);
                var text=item.GetComponent<TextMeshProUGUI>(); text.font=template.font; text.fontSharedMaterial=template.fontSharedMaterial;
                text.enableWordWrapping=false; text.raycastTarget=false; text.alignment=TextAlignmentOptions.TopLeft;
                rows.Add(text);
            }
        }
        Synchronize();
        // The owner coalesces activity snapshots; avoid text/mesh allocation on every HUD frame.
        if(ReferenceEquals(shown,progress)) return;
        shown=progress;
        if(cancel!=null) cancel.interactable=progress.Phase=="Preparing" || progress.Phase=="Generating" || progress.Phase=="ReturningToMenu" || progress.Phase=="MenuFailed";
        if(cancelText!=null) cancelText.text=progress.Phase=="Stopping" ? "Stopping generation…" : progress.Phase=="ReturningToMenu" || progress.Phase=="MenuFailed" ? "Return to menu" : "Cancel and return to menu";
        for(int i=0;i<rows.Count;i++)
        {
            if(i>=progress.Islands.Count) { rows[i].text=progress.Phase=="ReturningToMenu" || progress.Phase=="MenuFailed" ? "Road generation stopped; unpublished roads discarded." : "Preparing island work…"; continue; }
            var row=progress.Islands[i];
            if(row.Assignment==0) { rows[i].text="Worker "+(i+1)+" · waiting"; continue; }
            string stage=row.Stage=="Pricing" ? $"Pricing {row.PricesCompleted}/{row.PricesTotal}" : row.Stage;
            rows[i].text=$"Island {row.Island} · {row.Processed}/{row.Destinations} processed · {row.Reached} reached · {row.Kilometres:F1} km\n<size=85%>{stage} · {row.SearchSteps:N0} steps · {row.ActiveSearches} searching · {row.StageSeconds:F0}s</size>";
        }
    }
    private static void Relayout()
    {
        if(viewport==null || content==null) return;
        Vector2 size=viewport.rect.size;
        if(size==layoutSize) return;
        layoutSize=size;
        int columns=rows.Count>8?2:1;
        int lines=(rows.Count+columns-1)/columns;
        // Recompute from the current viewport: no stale sizing after a resolution/UI-scale change.
        float rowHeight=Math.Min(52f,Math.Max(1f,size.y)/8f);
        content.sizeDelta=new Vector2(0,Math.Max(size.y,lines*rowHeight));
        for(int i=0;i<rows.Count;i++)
        {
            var cell=(RectTransform)rows[i].transform;
            int column=i/lines,row=i%lines;
            cell.anchorMin=new Vector2(column/(float)columns,1); cell.anchorMax=new Vector2((column+1)/(float)columns,1);
            cell.pivot=new Vector2(.5f,1); cell.offsetMin=new Vector2(6,-(row+1)*rowHeight+2); cell.offsetMax=new Vector2(-6,-row*rowHeight);
            rows[i].fontSize=Math.Min(columns==1?22f:18f,rowHeight/2.3f);
        }
    }

}
