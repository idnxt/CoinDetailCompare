using System.ComponentModel;

namespace CoinDetailCompare.App.Services;

/// <summary>
/// Bindable view over <see cref="LocalizationService"/>.
///
/// Every localization key is exposed as a named property so XAML can bind it
/// without indexer quotes (which are not allowed inside MarkupExtension):
///   Text="{Binding CaptureSection, Source={StaticResource Strings}}"
///
/// When the language changes, PropertyChanged is raised for every key,
/// so all bound labels update in place.
/// </summary>
public class Strings : INotifyPropertyChanged
{
    private readonly LocalizationService _loc;

    public Strings(LocalizationService loc)
    {
        _loc = loc;
    }

    public string this[string key] => _loc.T(key);

    public void SetLanguage(string lang)
    {
        _loc.SetLanguage(lang);
        foreach (var key in _loc.Keys)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // app
    public string AppTitle => this["app.title"];

    // capture
    public string CaptureAlbum => this["capture.album"];
    public string CapturePage => this["capture.page"];
    public string CaptureOrder => this["capture.order"];
    public string CaptureShootObverse => this["capture.shoot_obverse"];
    public string CaptureShootReverse => this["capture.shoot_reverse"];
    public string CaptureLoadObverse => this["capture.load_obverse"];
    public string CaptureLoadReverse => this["capture.load_reverse"];
    public string CaptureSwapSides => this["capture.swap_sides"];
    public string CaptureOpenCamera => this["capture.open_camera"];
    public string CaptureObverse => this["capture.obverse"];
    public string CaptureReverse => this["capture.reverse"];
    public string CaptureCameraOffline => this["capture.camera_offline"];
    public string CaptureWindowTitle => this["capture.window_title"];
    public string CaptureShootObverseKey => this["capture.shoot_obverse_key"];
    public string CaptureShootReverseKey => this["capture.shoot_reverse_key"];
    public string CaptureClose => this["capture.close"];
    public string CaptureNext => this["capture.next"];
    public string CaptureExpand => this["capture.expand"];
    public string CaptureCollapse => this["capture.collapse"];
    public string CaptureHotkeysHint => this["capture.hotkeys_hint"];

    // compare
    public string CompareSection => this["compare.section"];
    public string CompareCurrent => this["compare.current"];
    public string CompareBase => this["compare.base"];
    public string CompareMatchObverse => this["compare.match_obverse"];
    public string CompareMatchReverse => this["compare.match_reverse"];
    public string CompareOrientation => this["compare.orientation"];
    public string CompareSearch => this["compare.search"];
    public string CompareRebuild => this["compare.rebuild"];
    public string CompareDevice => this["compare.device"];
    public string ComparePatches => this["compare.patches"];
    public string CompareSwap => this["compare.swap"];
    public string CompareHeatmap => this["compare.heatmap"];
    public string CompareLayer => this["compare.layer"];
    public string CompareCandidates => this["compare.candidates"];
    public string CompareCandidateHint => this["compare.candidate_hint"];
    public string CompareEtalonSet => this["compare.etalon_set"];
    public string CompareDiff => this["compare.diff"];
    public string CompareVector => this["compare.vector"];
    public string CompareVectorHint => this["compare.vector_hint"];
    public string CompareAnnotationHint => this["compare.annotation_hint"];

    // annotation
    public string AnnotationToolLine => this["annotation.tool_line"];
    public string AnnotationToolEllipse => this["annotation.tool_ellipse"];
    public string AnnotationToolArrow => this["annotation.tool_arrow"];
    public string AnnotationUndo => this["annotation.undo"];
    public string AnnotationClear => this["annotation.clear"];
    public string AnnotationSaveLayer => this["annotation.save_layer"];

    // actions
    public string ActionsSection => this["actions.section"];
    public string ActionsSwapSides => this["actions.swap_sides"];
    public string ActionsSaveMatch => this["actions.save_match"];
    public string ActionsDuplicate => this["actions.duplicate"];
    public string ActionsVariant => this["actions.variant"];
    public string ActionsAnomaly => this["actions.anomaly"];
    public string ActionsNotDetermined => this["actions.not_determined"];
    public string ActionsSaveCoin => this["actions.save_coin"];
    public string ActionsRebuildIndex => this["actions.rebuild_index"];
    public string ActionsAnomalyDesc => this["actions.anomaly_desc"];

    // numista
    public string NumistaSection => this["numista.section"];
    public string NumistaManualLink => this["numista.manual_link"];
    public string NumistaLinkExample => this["numista.link_example"];
    public string NumistaValidateLink => this["numista.validate_link"];
    public string NumistaSearchByApi => this["numista.search_by_api"];
    public string NumistaSearchPlaceholder => this["numista.search_placeholder"];
    public string NumistaSearch => this["numista.search"];
    public string NumistaApplyItem => this["numista.apply_item"];

    // coin
    public string CoinSection => this["coin.section"];
    public string CoinMintYear => this["coin.mint_year"];
    public string CoinMint => this["coin.mint"];
    public string CoinQuality => this["coin.quality"];
    public string CoinAnomalyDesc => this["coin.anomaly_desc"];
    public string CoinSaveAttributes => this["coin.save_attributes"];

    // type
    public string TypeSection => this["type.section"];
    public string TypeName => this["type.name"];
    public string TypeIssuer => this["type.issuer"];
    public string TypePeriod => this["type.period"];
    public string TypeValue => this["type.value"];
    public string TypeCurrency => this["type.currency"];
    public string TypeDiameter => this["type.diameter"];
    public string TypeYears => this["type.years"];
    public string TypeComposition => this["type.composition"];
    public string TypeMass => this["type.mass"];
    public string TypeThickness => this["type.thickness"];
    public string TypeShape => this["type.shape"];
    public string TypeTechnique => this["type.technique"];
    public string TypeOrientation => this["type.orientation"];
    public string TypeNumistaUrl => this["type.numista_url"];
    public string TypeLoadType => this["type.load_type"];
    public string TypeSaveType => this["type.save_type"];

    // anomaly
    public string AnomalyCheck => this["anomaly.check"];
    public string AnomalyDetected => this["anomaly.detected"];
    public string AnomalyOk => this["anomaly.ok"];
    public string AnomalyScore => this["anomaly.score"];

    // status
    public string StatusBackendOk => this["status.backend_ok"];
    public string StatusBackendDown => this["status.backend_down"];
    public string StatusSaved => this["status.saved"];
    public string StatusError => this["status.error"];
    public string StatusLanguage => this["status.language"];

    // catalog
    public string CatalogTitle => this["catalog.title"];
    public string CatalogRefresh => this["catalog.refresh"];
    public string CatalogCount => this["catalog.count"];
    public string CatalogCode => this["catalog.code"];
    public string CatalogKind => this["catalog.kind"];
    public string CatalogName => this["catalog.name"];
    public string CatalogYear => this["catalog.year"];
    public string CatalogEdit => this["catalog.edit"];
    public string CatalogTreeHint => this["catalog.tree_hint"];

    // kind
    public string KindLabel => this["kind.label"];
    public string KindEtalon => this["kind.etalon"];
    public string KindDuplicate => this["kind.duplicate"];
    public string KindAnomaly => this["kind.anomaly"];

    // card
    public string CardTitle => this["card.title"];
    public string CardEtalon => this["card.etalon"];
    public string CardNoEtalon => this["card.no_etalon"];
    public string CardCompareDone => this["card.compare_done"];
    public string CardSave => this["card.save"];
    public string CardAnnotations => this["card.annotations"];
    public string CardRunCompare => this["card.run_compare"];
    public string CardRegenVector => this["card.regenerate_vector"];
    public string CardVectorRecreated => this["card.vector_recreated"];
    public string CardLayerDiff => this["card.layer_diff"];
    public string CardDiffUnavailable => this["card.diff_unavailable"];
    public string CardLayerEtalonPhoto => this["card.layer_etalon_photo"];
    public string CardOpacity => this["card.opacity"];
    public string CardReset => this["card.reset"];
    public string CardEtalonPhotoHint => this["card.etalon_photo_hint"];
    public string CardAngle => this["card.angle"];
    public string CardScale => this["card.scale"];
    public string CardSimilar => this["card.similar"];
    public string CardFindSimilar => this["card.find_similar"];
    public string CardPickEtalon => this["card.pick_etalon"];
    public string CardResetEtalon => this["card.reset_etalon"];
    public string CardSimilarFound => this["card.similar_found"];
    public string CardSimilarNone => this["card.similar_none"];
    public string CardEtalonSet => this["card.etalon_set"];
    public string CardEtalonReset => this["card.etalon_reset"];

    // backend
    public string BackendStart => this["backend.start"];
    public string BackendStop => this["backend.stop"];
    public string BackendStarting => this["backend.starting"];
    public string BackendStopping => this["backend.stopping"];
    public string BackendUp => this["backend.up"];
    public string BackendDown => this["backend.down"];

    // gpu
    public string GpuSetupTitle => this["gpu.title"];
    public string GpuConfirm => this["gpu.confirm"];
    public string GpuSizes => this["gpu.sizes"];
    public string GpuStarting => this["gpu.starting"];
    public string GpuRestarting => this["gpu.restarting"];
    public string GpuReady => this["gpu.ready"];
    public string GpuRestartFailed => this["gpu.restart_failed"];
    public string GpuFailed => this["gpu.failed"];
}
