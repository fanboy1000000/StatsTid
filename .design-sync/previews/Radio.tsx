import { Radio } from 'statstid-frontend'

const noop = () => {}

export const Group = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    <Radio id="r-ac" name="overenskomst" value="AC" label="AC (Akademikere)" checked onChange={noop} />
    <Radio id="r-hk" name="overenskomst" value="HK" label="HK (Kontor)" checked={false} onChange={noop} />
    <Radio id="r-prosa" name="overenskomst" value="PROSA" label="PROSA (IT)" checked={false} onChange={noop} />
  </div>
)

export const Selected = () => (
  <Radio id="r-selected" name="valg" value="ja" label="Fuldtid" checked onChange={noop} />
)

export const Unselected = () => (
  <Radio id="r-unselected" name="valg" value="nej" label="Deltid" checked={false} onChange={noop} />
)

export const Disabled = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    <Radio id="r-disabled-on" name="status" value="aktiv" label="Aktiv" checked onChange={noop} disabled />
    <Radio id="r-disabled-off" name="status" value="inaktiv" label="Inaktiv" checked={false} onChange={noop} disabled />
  </div>
)
