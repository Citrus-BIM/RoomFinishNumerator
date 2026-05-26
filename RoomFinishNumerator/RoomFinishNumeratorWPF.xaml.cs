using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RoomFinishNumerator
{
    public partial class RoomFinishNumeratorWPF : Window
    {
        public string RoomFinishNumberingSelectedName;

        public bool ConsiderCeilings;
        public bool ConsiderOpenings;
        public bool ConsiderBaseboards;
        public bool ConsiderPhase;
        public ElementId SelectedPhaseId = ElementId.InvalidElementId;
        public ElementId SelectedPhaseFilterId = ElementId.InvalidElementId;

        private readonly List<PhaseSelectionItem> _phaseItems;
        private readonly List<PhaseFilterSelectionItem> _phaseFilterItems;

        public RoomFinishNumeratorWPF()
            : this(new List<PhaseSelectionItem>(), ElementId.InvalidElementId, new List<PhaseFilterSelectionItem>(), ElementId.InvalidElementId)
        {
        }

        public RoomFinishNumeratorWPF(
            IEnumerable<PhaseSelectionItem> phaseItems,
            ElementId defaultPhaseId,
            IEnumerable<PhaseFilterSelectionItem> phaseFilterItems,
            ElementId defaultPhaseFilterId)
        {
            InitializeComponent();
            _phaseItems = phaseItems.ToList();
            _phaseFilterItems = phaseFilterItems.ToList();

            comboBox_Phase.ItemsSource = _phaseItems;
            comboBox_Phase.DisplayMemberPath = nameof(PhaseSelectionItem.Name);
            comboBox_PhaseFilter.ItemsSource = _phaseFilterItems;
            comboBox_PhaseFilter.DisplayMemberPath = nameof(PhaseFilterSelectionItem.Name);

            var defaultPhaseItem = _phaseItems.FirstOrDefault(p => ElementIdCompat.HasSameValue(p.PhaseId, defaultPhaseId))
                ?? _phaseItems.LastOrDefault();
            comboBox_Phase.SelectedItem = defaultPhaseItem;

            var defaultPhaseFilterItem = _phaseFilterItems.FirstOrDefault(pf => ElementIdCompat.HasSameValue(pf.PhaseFilterId, defaultPhaseFilterId))
                ?? _phaseFilterItems.FirstOrDefault();
            comboBox_PhaseFilter.SelectedItem = defaultPhaseFilterItem;

            checkBox_ConsiderPhase.IsEnabled = _phaseItems.Any();
            UpdatePhaseSelectorState();
        }

        private void btn_Ok_Click(object sender, RoutedEventArgs e)
        {
            ConfirmAndClose();
        }

        private void btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RoomFinishNumeratorWPF_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                ConfirmAndClose();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void checkBox_ConsiderPhase_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdatePhaseSelectorState();
        }

        private void UpdatePhaseSelectorState()
        {
            var isEnabled = checkBox_ConsiderPhase.IsChecked == true;
            comboBox_Phase.IsEnabled = isEnabled && _phaseItems.Any();
            comboBox_PhaseFilter.IsEnabled = isEnabled && _phaseFilterItems.Any();
        }

        private void ConfirmAndClose()
        {
            var selectedNumberingRadioButton = (groupBox_RoomFinishNumbering.Content as StackPanel)
                .Children.OfType<RadioButton>()
                .FirstOrDefault(rb => rb.IsChecked == true);

            if (selectedNumberingRadioButton == null)
            {
                return;
            }

            RoomFinishNumberingSelectedName = selectedNumberingRadioButton.Name;
            ConsiderCeilings = checkBox_ConsiderCeilings.IsChecked == true;
            ConsiderOpenings = checkBox_ConsiderOpenings.IsChecked == true;
            ConsiderBaseboards = checkBox_ConsiderBaseboards.IsChecked == true;
            ConsiderPhase = checkBox_ConsiderPhase.IsChecked == true;

            if (ConsiderPhase)
            {
                var selectedPhase = comboBox_Phase.SelectedItem as PhaseSelectionItem;
                var selectedPhaseFilter = comboBox_PhaseFilter.SelectedItem as PhaseFilterSelectionItem;
                if (selectedPhase == null)
                {
                    MessageBox.Show("Выберите стадию для нумерации.", "Нумератор отделки", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectedPhaseId = selectedPhase.PhaseId;
                SelectedPhaseFilterId = selectedPhaseFilter?.PhaseFilterId ?? ElementId.InvalidElementId;
            }
            else
            {
                SelectedPhaseId = ElementId.InvalidElementId;
                SelectedPhaseFilterId = ElementId.InvalidElementId;
            }

            DialogResult = true;
            Close();
        }
    }
}
