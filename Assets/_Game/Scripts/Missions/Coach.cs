namespace OpenSkies.Missions
{
    /// <summary>
    /// Chooses the one coaching message on screen, in this order:
    /// 1. Safety: what to do about an active warning (the banner is the alert).
    /// 2. Success: a short confirmation, for a couple of seconds.
    /// 3. Hint: extra help when the player seems to struggle, for a few seconds.
    /// 4. The explanation the player asked for (tap), until tapped again.
    /// 5. The current instruction, unless the mission says the player no longer needs it.
    /// Nothing else is shown, nothing flashes, and <see cref="CoachingCue.Version"/> only
    /// changes with the content, so the HUD updates text only when it has to.
    /// Plain C#, allocation-free (all strings are cached by the mission).
    /// </summary>
    public sealed class Coach
    {
        string _label = string.Empty;
        string _detail;
        bool _detailShown;
        string _instruction;
        CoachHighlight _instructionHighlights;
        bool _quiet;
        string _safety;
        string _success;
        float _successLeft;
        string _hint;
        CoachHighlight _hintHighlights;
        float _hintLeft;
        bool _enabled;
        CoachingCue _cue;

        public CoachingCue Cue => _cue;
        public bool ShowingHint => _hintLeft > 0f && _hint != null;
        public bool ShowingSuccess => _successLeft > 0f && _success != null;
        public bool ShowingSafety => !string.IsNullOrEmpty(_safety);
        public string Instruction => _instruction;

        /// <summary>Starts or stops coaching. Stopping clears everything.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) Clear();
            Refresh();
        }

        /// <summary>A new step: new label and explanation; the explanation is closed and pending hints are dropped.</summary>
        public void BeginStep(string label, string detail)
        {
            _label = label ?? string.Empty;
            _detail = string.IsNullOrEmpty(detail) ? null : detail;
            _detailShown = false;
            _hint = null;
            _hintLeft = 0f;
            Refresh();
        }

        public void SetInstruction(string text, CoachHighlight highlights, bool quiet)
        {
            _instruction = string.IsNullOrEmpty(text) ? null : text;
            _instructionHighlights = highlights;
            _quiet = quiet;
        }

        /// <summary>The safety text for the most urgent warning, or null when none is active.</summary>
        public void SetSafety(string text) => _safety = string.IsNullOrEmpty(text) ? null : text;

        public void ShowSuccess(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            _success = text;
            _successLeft = seconds;
            Refresh();
        }

        public void ShowHint(string text, CoachHighlight highlights, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            _hint = text;
            _hintHighlights = highlights;
            _hintLeft = seconds;
            Refresh();
        }

        /// <summary>The player tapped the message: show or hide the short explanation.</summary>
        public void ToggleDetail()
        {
            if (_detail == null) return;
            _detailShown = !_detailShown;
            Refresh();
        }

        public void Clear()
        {
            _label = string.Empty;
            _detail = null;
            _detailShown = false;
            _instruction = null;
            _quiet = false;
            _safety = null;
            _success = null;
            _successLeft = 0f;
            _hint = null;
            _hintLeft = 0f;
        }

        /// <summary>Advances the timers and recomputes the cue.</summary>
        public void Update(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                if (_successLeft > 0f) _successLeft -= deltaTime;
                if (_hintLeft > 0f) _hintLeft -= deltaTime;
            }
            Refresh();
        }

        void Refresh()
        {
            bool visible = false;
            string text = null;
            var tone = CoachTone.Instruction;
            var highlights = CoachHighlight.None;
            bool showingDetail = false;

            if (_enabled)
            {
                if (_safety != null)
                {
                    visible = true; text = _safety; tone = CoachTone.Safety;
                }
                else if (_successLeft > 0f && _success != null)
                {
                    visible = true; text = _success; tone = CoachTone.Success;
                }
                else if (_hintLeft > 0f && _hint != null)
                {
                    visible = true; text = _hint; tone = CoachTone.Hint; highlights = _hintHighlights;
                }
                else if (_detailShown && _detail != null)
                {
                    visible = true; text = _detail; showingDetail = true;
                }
                else if (_instruction != null && !_quiet)
                {
                    visible = true; text = _instruction; highlights = _instructionHighlights;
                }
            }

            if (text == null) text = string.Empty;
            bool hasDetail = _enabled && _detail != null;
            var c = _cue;
            if (c.Visible == visible && ReferenceEquals(c.Text, text) && c.Tone == tone && c.Highlights == highlights
                && ReferenceEquals(c.Label, _label) && c.HasDetail == hasDetail && c.ShowingDetail == showingDetail)
                return;
            _cue = new CoachingCue
            {
                Visible = visible,
                Label = _label,
                Text = text,
                Tone = tone,
                Highlights = highlights,
                HasDetail = hasDetail,
                ShowingDetail = showingDetail,
                Version = c.Version + 1,
            };
        }
    }
}
