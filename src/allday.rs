//! The all-day strip of the calendar day and week header: how many rows it shows and which events
//! of each day stay behind a "+N more" row. Bulwark caps the strip at three rows with a toggle that
//! shows the rest (`components/calendar/calendar-week-view.tsx:343-347` for the cap and the height
//! `max(28, rows * 24 + 4)`, `:575-593` for the "+N" / "less" toggle; decision D-M4-08).
//! Pure: the calendar view feeds it the number of all-day items of each day column.

/// Rows shown before the strip needs expanding (Bulwark `DEFAULT_ALL_DAY_MAX_ROWS`).
pub const MAX_ROWS: usize = 3;
/// Height of one row in px (Bulwark `top: row * 24 + 2`, chip height 20).
pub const ROW_PX: f32 = 24.0;

/// What the strip draws for one week or day.
#[derive(Debug, Clone, PartialEq)]
pub struct StripLayout {
    /// per day column: how many of its items are drawn as chips (the first ones)
    pub shown: Vec<usize>,
    /// per day column: how many items sit behind its "+N more" row (0 = no such row); when the strip
    /// is collapsed and a day has more than `MAX_ROWS` items, the last row is that "+N more" row
    pub more: Vec<usize>,
    /// rows the strip is tall now (collapsed: at most `MAX_ROWS`)
    pub rows: usize,
    /// rows the fullest day needs
    pub total_rows: usize,
    /// whether there is anything to expand (the toggle shows)
    pub expandable: bool,
    /// the toggle's "+N" (Bulwark: `maxContentRows - 3`)
    pub extra_rows: usize,
    /// strip height in px, 0 when no day has an all-day item (the strip is not drawn)
    pub height: f32,
}

/// Lays out the strip from the all-day item count of each day column.
pub fn layout(counts: &[usize], expanded: bool) -> StripLayout {
    let total_rows = counts.iter().copied().max().unwrap_or(0);
    let expandable = total_rows > MAX_ROWS;
    let open = expanded && expandable;
    let rows = if open { total_rows } else { total_rows.min(MAX_ROWS) };
    let collapsed_cap = expandable && !open;
    let mut shown = Vec::with_capacity(counts.len());
    let mut more = Vec::with_capacity(counts.len());
    for &n in counts {
        if collapsed_cap && n > MAX_ROWS {
            // the third row becomes "+N more", so the day shows two items and counts the rest
            shown.push(MAX_ROWS - 1);
            more.push(n - (MAX_ROWS - 1));
        } else {
            shown.push(n);
            more.push(0);
        }
    }
    let height = if total_rows == 0 { 0.0 } else { (rows as f32 * ROW_PX + 4.0).max(28.0) };
    StripLayout { shown, more, rows, total_rows, expandable, extra_rows: total_rows.saturating_sub(MAX_ROWS), height }
}

/// The label of a day's "+N more" row.
pub fn more_label(n: usize) -> String { if n == 0 { String::new() } else { format!("+{n} more") } }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_strip_has_no_height() {
        let l = layout(&[0, 0, 0], false);
        assert_eq!((l.rows, l.height, l.expandable), (0, 0.0, false));
        assert_eq!(layout(&[], true).height, 0.0);
    }

    #[test]
    fn up_to_three_rows_is_not_capped() {
        let l = layout(&[1, 3, 2], false);
        assert_eq!(l.shown, vec![1, 3, 2]);
        assert_eq!(l.more, vec![0, 0, 0]);
        assert_eq!((l.rows, l.expandable, l.extra_rows), (3, false, 0));
        assert_eq!(l.height, 76.0); // 3 * 24 + 4
    }

    #[test]
    fn one_row_is_28px_minimum() {
        let l = layout(&[1], false);
        assert_eq!(l.height, 28.0); // max(28, 24 + 4)
    }

    #[test]
    fn collapsed_caps_at_three_rows_with_more_row() {
        let l = layout(&[5, 1, 4, 3], false);
        assert_eq!(l.rows, 3);
        assert!(l.expandable);
        assert_eq!(l.extra_rows, 2);
        assert_eq!(l.height, 76.0);
        // a day over the cap shows two items and "+N more" in the third row
        assert_eq!(l.shown, vec![2, 1, 2, 3]);
        assert_eq!(l.more, vec![3, 0, 2, 0]);
        // every item is either drawn or counted
        for (i, n) in [5usize, 1, 4, 3].iter().enumerate() { assert_eq!(l.shown[i] + l.more[i], *n); }
    }

    #[test]
    fn expanded_shows_every_row() {
        let l = layout(&[5, 1], true);
        assert_eq!(l.shown, vec![5, 1]);
        assert_eq!(l.more, vec![0, 0]);
        assert_eq!((l.rows, l.total_rows, l.expandable), (5, 5, true));
        assert_eq!(l.height, 124.0); // 5 * 24 + 4
    }

    #[test]
    fn expanding_a_strip_that_fits_changes_nothing() {
        assert_eq!(layout(&[2, 3], true), layout(&[2, 3], false));
    }

    #[test]
    fn four_items_is_the_smallest_cap() {
        let l = layout(&[4], false);
        assert_eq!((l.shown[0], l.more[0], l.extra_rows), (2, 2, 1));
    }

    #[test]
    fn more_label_text() {
        assert_eq!(more_label(0), "");
        assert_eq!(more_label(3), "+3 more");
    }
}
