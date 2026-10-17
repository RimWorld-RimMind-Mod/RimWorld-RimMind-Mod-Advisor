using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimMind.Advisor.Advisor
{
    public class Window_AdvisorApproval : Window
    {
        private readonly CabinetProposal _proposal;
        private readonly Pawn _advisor;
        private readonly Action _onApproved;
        private readonly Action _onDismissed;
        private readonly bool[] _selectedItems;
        private Vector2 _scrollPosition = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(560f, 440f);

        public Window_AdvisorApproval(
            CabinetProposal proposal,
            Pawn advisor,
            Action onApproved,
            Action onDismissed)
        {
            _proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
            _advisor = advisor;
            _onApproved = onApproved;
            _onDismissed = onDismissed;
            _selectedItems = new bool[_proposal.Items.Count];
            for (int i = 0; i < _selectedItems.Length; i++) _selectedItems[i] = true;

            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 32f), _proposal.Title);
            Text.Font = GameFont.Small;

            // Summary
            float curY = 36f;
            if (!string.IsNullOrWhiteSpace(_proposal.Summary))
            {
                Widgets.Label(new Rect(0, curY, inRect.width, 40f), _proposal.Summary);
                curY += 44f;
            }

            // Scrollable list of items
            float listHeight = inRect.height - curY - 50f;
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, Math.Max(listHeight, _proposal.Items.Count * 44f));
            Rect outRect = new Rect(0, curY, inRect.width, listHeight);

            Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);
            float itemY = 0f;
            for (int i = 0; i < _proposal.Items.Count; i++)
            {
                var item = _proposal.Items[i];
                Rect rowRect = new Rect(0, itemY, viewRect.width, 38f);
                Widgets.DrawBoxSolid(rowRect, i % 2 == 0 ? new Color(0.15f, 0.15f, 0.15f, 0.4f) : new Color(0.2f, 0.2f, 0.2f, 0.4f));

                Rect checkRect = new Rect(4f, itemY + 8f, 24f, 24f);
                Widgets.Checkbox(checkRect.position, ref _selectedItems[i]);

                Rect labelRect = new Rect(34f, itemY + 2f, viewRect.width - 40f, 34f);
                string text = $"<b>[{item.Action}]</b>: {item.Reason ?? item.Target ?? item.Param ?? ""}";
                Widgets.Label(labelRect, text);

                itemY += 42f;
            }
            Widgets.EndScrollView();

            // Bottom action buttons
            float btnY = inRect.height - 38f;
            float btnWidth = 140f;

            string approveSelectedLabel = "RimMind.Advisor.Cabinet.ApproveSelected".CanTranslate()
                ? "RimMind.Advisor.Cabinet.ApproveSelected".Translate().ToString()
                : "Approve Selected";
            string approveAllLabel = "RimMind.Advisor.Cabinet.ApproveAll".CanTranslate()
                ? "RimMind.Advisor.Cabinet.ApproveAll".Translate().ToString()
                : "Approve All";
            string dismissLabel = "RimMind.Advisor.Cabinet.Dismiss".CanTranslate()
                ? "RimMind.Advisor.Cabinet.Dismiss".Translate().ToString()
                : "Dismiss";

            if (Widgets.ButtonText(new Rect(0, btnY, btnWidth, 34f), approveSelectedLabel))
            {
                for (int i = 0; i < _proposal.ExecutionCallbacks.Count; i++)
                {
                    if (i < _selectedItems.Length && _selectedItems[i])
                    {
                        try { _proposal.ExecutionCallbacks[i](); }
                        catch (Exception ex) { Log.Warning($"[Window_AdvisorApproval] Action failed: {ex.Message}"); }
                    }
                }
                _onApproved?.Invoke();
                Close();
            }

            if (Widgets.ButtonText(new Rect(btnWidth + 10f, btnY, btnWidth, 34f), approveAllLabel))
            {
                foreach (var cb in _proposal.ExecutionCallbacks)
                {
                    try { cb(); }
                    catch (Exception ex) { Log.Warning($"[Window_AdvisorApproval] Action failed: {ex.Message}"); }
                }
                _onApproved?.Invoke();
                Close();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - btnWidth, btnY, btnWidth, 34f), dismissLabel))
            {
                _onDismissed?.Invoke();
                Close();
            }
        }
    }
}
