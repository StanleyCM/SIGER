export default function SiteFooter({ onHome, disabled }) {
  return <footer className="site-footer"><button type="button" className="wordmark" disabled={disabled} onClick={onHome} aria-label="Superior Kitchen Essentials, volver al inicio">Superior<span>Kitchen Essentials</span></button><p>Buenos encuentros. Buena mesa.<small>Powered by SIGER</small></p></footer>
}
