const fs = require('node:fs');
const path = require('node:path');
const {Resvg} = require('@resvg/resvg-js');

// Canonical vector masters and required runtime exports stay in the repository.
// No media-production intermediates, live screenshots or account data are saved.
const root = path.resolve(__dirname, '..');
const branding = path.join(root, 'assets/branding');
const source = fs.readFileSync(path.join(branding, 'dashboard-icon.svg'));
const render = (svg, size) => Buffer.from(new Resvg(svg, {fitTo:{mode:'width',value:size}}).render().asPng());
const sizes = new Map([16,32,48,64,128,256,512,1024].map(size => [size,render(source,size)]));
fs.writeFileSync(path.join(branding,'dashboard-icon.png'),sizes.get(1024));
fs.writeFileSync(path.join(branding,'dashboard-mark.png'),render(fs.readFileSync(path.join(branding,'dashboard-mark.svg')),64));

// ICNS stores PNG representations, with their native/Retina resolution tags.
function chunk(type,data) {
  const header=Buffer.alloc(8);header.write(type,0,'ascii');header.writeUInt32BE(data.length+8,4);
  return Buffer.concat([header,data]);
}
const records=[['icp4',16],['icp5',32],['icp6',64],['ic07',128],['ic08',256],['ic09',512],['ic10',1024],['ic11',32],['ic12',64],['ic13',256],['ic14',512]].map(([type,size])=>chunk(type,sizes.get(size)));
const icns=Buffer.concat(records);const header=Buffer.alloc(8);header.write('icns');header.writeUInt32BE(icns.length+8,4);
fs.writeFileSync(path.join(branding,'dashboard-icon.icns'),Buffer.concat([header,icns]));

// A PNG-backed multi-resolution Windows ICO uses the same vector master.
const windowsSizes=[16,32,48,64,128,256];
const icoHeader=Buffer.alloc(6);icoHeader.writeUInt16LE(1,2);icoHeader.writeUInt16LE(windowsSizes.length,4);
let offset=6+16*windowsSizes.length;
const entries=windowsSizes.map(size=>{
  const image=sizes.get(size), entry=Buffer.alloc(16);
  entry[0]=size===256?0:size;entry[1]=entry[0];entry.writeUInt16LE(1,4);entry.writeUInt16LE(32,6);
  entry.writeUInt32LE(image.length,8);entry.writeUInt32LE(offset,12);offset+=image.length;
  return entry;
});
fs.writeFileSync(path.join(branding,'dashboard-icon.ico'),Buffer.concat([icoHeader,...entries,...windowsSizes.map(size=>sizes.get(size))]));
fs.copyFileSync(path.join(branding,'dashboard-icon.svg'),path.join(root,'ui/web/dashboard-icon.svg'));
console.log('Updated canonical dashboard PNG, ICNS, ICO, menu symbol and web logo.');
