//! Copies the app Theme's neutral colours into CTheme (ui/ctheme.slint), the palette of the
//! composer extras, so those .slint files need not import app.slint and always follow
//! whatever the Theme tokens are.
use crate::{AppWindow, CTheme, Theme};
use slint::ComponentHandle;

pub fn sync(ui: &AppWindow) {
    let t = ui.global::<Theme>();
    let c = ui.global::<CTheme>();
    c.set_dark(t.get_dark());
    c.set_text(t.get_text());
    c.set_dim(t.get_dim());
    c.set_line(t.get_line());
    c.set_popup(t.get_popup());
    c.set_hover(t.get_hover());
    c.set_raised(t.get_raised());
    c.set_chip(t.get_chip());
    c.set_accent(t.get_accent());
    c.set_on_accent(t.get_on_accent());
    c.set_sel(t.get_sel());
    c.set_sel_text(t.get_sel_text());
    c.set_ring(t.get_ring());
    c.set_bg(t.get_bg());
    c.set_icons(t.get_icons());
}
