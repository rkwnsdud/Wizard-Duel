// Offline authoring of the three right arm rotation tracks. No scene or weapon writes.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const ROOT = path.resolve(__dirname, '..');
const DIR = path.join(ROOT, 'Assets/Game/Weapons/BasicWand/Art/Animations');
const add=(a,b)=>a.map((v,i)=>v+b[i]), sub=(a,b)=>a.map((v,i)=>v-b[i]);
const mul=(a,s)=>a.map(v=>v*s), dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0);
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const norm=a=>Math.hypot(...a), unit=a=>mul(a,1/norm(a));
const qi=q=>[-q[0],-q[1],-q[2],q[3]];
const qm=(a,b)=>[...add(add(mul(b.slice(0,3),a[3]),mul(a.slice(0,3),b[3])),cross(a,b)),a[3]*b[3]-dot(a.slice(0,3),b.slice(0,3))];
const rotate=(q,v)=>qm(qm(q,[...v,0]),qi(q)).slice(0,3);
const axis=(v,t)=>[...mul(unit(v),Math.sin(t/2)),Math.cos(t/2)];
const between=(a,b)=>{a=unit(a);b=unit(b);const d=dot(a,b);if(d<-.999999)return axis(Math.abs(a[0])<.9?cross(a,[1,0,0]):cross(a,[0,1,0]),Math.PI);return unit([...cross(a,b),1+d]);};
const angle=(a,b)=>Math.acos(Math.min(1,Math.max(-1,dot(unit(a),unit(b)))))*180/Math.PI;
const qangle=(a,b)=>2*Math.acos(Math.min(1,Math.abs(dot(unit(a),unit(b)))))*180/Math.PI;
const slerp=(a,b,t)=>{if(dot(a,b)<0)b=mul(b,-1);const d=Math.min(1,dot(a,b));if(d>.9995)return unit(add(mul(a,1-t),mul(b,t)));const r=Math.acos(d);return add(mul(a,Math.sin((1-t)*r)/Math.sin(r)),mul(b,Math.sin(t*r)/Math.sin(r)));};
const project=(v,n)=>sub(v,mul(n,dot(v,n)));
const smooth=(a,b,t)=>{const u=Math.max(0,Math.min(1,(t-a)/(b-a)));return u*u*(3-2*u);};
const read=f=>fs.readFileSync(f,'utf8').replace(/\r\n/g,'\n');
const keyRE=()=>/(^      - serializedVersion: 3\n        time: ([^\n]+)\n        value: )([^\n]+)(\n        inSlope: )([^\n]+)(\n        outSlope: )([^\n]+)/gm;
const nums=s=>(s.match(/[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?/g)||[]).map(Number);
function section(text,start,end){const a=text.indexOf(start)+start.length,b=text.indexOf(end,a);assert(a>=start.length&&b>=a);return [a,b];}
function blocks(text,start,end,prefix='  - curve:\n'){const [a,b]=section(text,start,end);return text.slice(a,b).split(new RegExp('(?=^'+prefix+')','m'));}
const name=b=>b.match(/^    path: (.+)$/m)?.[1];
function curves(text,type){const ends={Rotation:'  m_CompressedRotationCurves:',Position:'  m_ScaleCurves:'};return Object.fromEntries(blocks(text,`  m_${type}Curves:\n`,ends[type]).filter(name).map(b=>[name(b),[...b.matchAll(keyRE())].map(m=>({t:+m[2],v:nums(m[3]),i:nums(m[5]),o:nums(m[7])}))]));}
// Evaluate Unity's unweighted cubic Hermite tracks, including between authored keys.
function sample(keys,t,quaternion=true){if(t<=keys[0].t)return keys[0].v;let b=keys.findIndex(k=>k.t>=t);if(b<0)return keys.at(-1).v;if(b===0)return keys[0].v;const a=keys[b-1];b=keys[b];const dt=b.t-a.t,u=(t-a.t)/dt;
 const v=a.v.map((v,j)=>(2*u**3-3*u*u+1)*v+(u**3-2*u*u+u)*dt*a.o[j]+(-2*u**3+3*u*u)*b.v[j]+(u**3-u*u)*dt*b.i[j]);return quaternion?unit(v):v;}
const chain=['WizardModel/Armature','WizardModel/Armature/Hips','WizardModel/Armature/Hips/Spine02','WizardModel/Armature/Hips/Spine02/Spine01','WizardModel/Armature/Hips/Spine02/Spine01/Spine','WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder'];
const ARM=chain.at(-1)+'/RightArm', ELBOW=ARM+'/RightForeArm', HAND=ELBOW+'/RightHand';
const idle=curves(read(path.join(DIR,'WizardCombatIdle_BasicWandGrip.anim')),'Rotation');
const idlePositions=curves(read(path.join(DIR,'WizardCombatIdle_BasicWandGrip.anim')),'Position');
// The finger row is diagonal in this rig: local +X alone is not the hand back.
const palmWidth=sub(idlePositions[HAND+'/RightLittleProximal'][0].v,idlePositions[HAND+'/RightIndexProximal'][0].v);
const handBack=unit(cross([0,1,0],palmWidth));
// Calibrate the real elbow hinge from the original cast, not an arbitrary
// shortest rotation between two segment directions.
const source=curves(read(path.join(ROOT,'Assets/Game/Weapons/Common/Art/BaseCast.anim')),'Rotation');
const sourceElbowDelta=qm(sample(source[ELBOW],.1),qi(idle[ELBOW][0].v));
const elbowHinge=unit([sourceElbowDelta[0],0,sourceElbowDelta[2]]);
function world(rot,pos,t){let q=[0,0,0,1],p=[0,0,0];const out={};for(const n of [...chain,ARM,ELBOW,HAND]){p=add(p,rotate(q,sample(pos[n],t,false)));q=qm(q,sample(rot[n],t));out[n]={q,p};}return out;}
const report=[];
const format=v=>Array.isArray(v)?'{'+v.map((n,i)=>`${'xyzw'[i]}: ${Number(n.toPrecision(9))}`).join(', ')+'}':String(Number(v.toPrecision(9)));
function track(samples){return samples.map((k,i)=>{const a=samples[Math.max(0,i-1)],b=samples[Math.min(samples.length-1,i+1)];const s=k.v.map((v,j)=>{if(i===0||i===samples.length-1)return 0;const left=(v-a.v[j])/(k.t-a.t),right=(b.v[j]-v)/(b.t-k.t);return left*right<=0?0:2*left*right/(left+right);});return {...k,i:s,o:s};});}
function rewrite(text,start,end,prefix,values,editor=false){const [a,b]=section(text,start,end);return text.slice(0,a)+blocks(text,start,end,prefix).map(block=>{
 const p=name(block);if(!values[p])return block;
 let keys=values[p];
 if(editor){const attr=block.match(/^    attribute: (.+)$/m)?.[1];if(!attr?.startsWith('m_LocalRotation.')&&!attr?.startsWith('localEulerAngles.'))return block;
 const times=[...block.matchAll(keyRE())].map(m=>+m[2]);let eulerLast;
 keys=times.map(t=>{const q=sample(values[p],t);let v;
 if(attr.startsWith('m_LocalRotation.'))v=q['xyzw'.indexOf(attr.at(-1))];
 else{const [x,y,z,w]=q;let e=[Math.asin(Math.max(-1,Math.min(1,2*(w*x-y*z)))),Math.atan2(2*(x*z+w*y),1-2*(x*x+y*y)),Math.atan2(2*(x*y+w*z),1-2*(x*x+z*z))].map(a=>a*180/Math.PI);if(eulerLast)e=e.map((n,i)=>n+360*Math.round((eulerLast[i]-n)/360));eulerLast=e;v=e['xyz'.indexOf(attr.at(-1))];}return {t,v:[v]};});keys=track(keys).map(k=>({...k,v:k.v[0],i:k.i[0],o:k.o[0]}));
 }
 let i=0;return block.replace(keyRE(),(all,prefix,t,v,ip,iv,op,ov)=>{const k=keys[i++];assert(Math.abs(k.t-(+t))<1e-5);return prefix+format(k.v)+ip+format(k.i)+op+format(k.o);});
 }).join('')+text.slice(b);}
for(const [file,wand] of [['BasicWandAttack.anim',[.115895,.861220,.226995,-.439708]]]){
 const filename=path.join(DIR,file), original=read(filename), rot=curves(original,'Rotation'), pos=curves(original,'Position');
 const neutral=idle[HAND][0].v, lower=unit(pos[HAND][0].v), shaft=rotate(qm(neutral,unit(wand)),[0,0,1]);
 console.log(file,'neutral wrist',angle(rotate(neutral,[0,1,0]),lower),'shaft/forearm',angle(shaft,lower));
 const release=world(rot,pos,.2), forward=[0,0,1];
 // A shallow raised wand presentation avoids forcing the original grip into
 // a backwards elbow bend. Projectile direction/event code is unchanged.
 const pitch=32, aim=rotate(axis([1,0,0],-pitch*Math.PI/180),forward);
 // Solve hand-back right/up and wand aim together. Elbow placement is relative
 // to the shoulder/body, not an extra requirement to fold it outside the wrist.
 let handQ=between(rotate(unit(wand),forward),aim);
 const backPlane=unit(project(rotate(handQ,handBack),aim)), backTarget=unit(project(unit([1,1,0]),aim));
 handQ=qm(axis(aim,Math.atan2(dot(aim,cross(backPlane,backTarget)),dot(backPlane,backTarget))),handQ);
 const handDirection=rotate(handQ,[0,1,0]);
 // A small wrist swing lets the forearm extend further forward while leaving
 // the hand orientation intact. No independent axial wrist roll is introduced.
 const wristAxis=rotate(axis(handDirection,340*Math.PI/180),unit(cross(handDirection,forward)));
 const wristSwing=axis(wristAxis,24*Math.PI/180);
 const lowerDir=rotate(wristSwing,handDirection);
 const elbowQ=qm(qm(wristSwing,handQ),qi(neutral));
 const elbowLocal=qm(axis(elbowHinge,18*Math.PI/180),idle[ELBOW][0].v);
 const armQ=qm(elbowQ,qi(elbowLocal));
 const targets={[ARM]:qm(qi(release[chain.at(-1)].q),armQ),[ELBOW]:elbowLocal,[HAND]:qm(qi(elbowQ),handQ)};
 const values={};
 for(const p of [ARM,ELBOW,HAND]){
  values[p]=track(rot[p].map(k=>{const t=k.t;const base=idle[p][0].v;
   const w=smooth(0,.2,t)*(1-smooth(.26,.55,t));let q=slerp(base,targets[p],w);return {t,v:q};}));
  for(let i=1;i<values[p].length;i++)if(dot(values[p][i-1].v,values[p][i].v)<0){values[p][i].v=mul(values[p][i].v,-1);values[p][i].i=mul(values[p][i].i,-1);values[p][i].o=mul(values[p][i].o,-1);}
 }
 let result=rewrite(original,'  m_RotationCurves:\n','  m_CompressedRotationCurves:','  - curve:\n',values);
 result=rewrite(result,'  m_EditorCurves:\n','  m_HasGenericRootTransform:','  - serializedVersion: 2\n',values,true);
 const actual=curves(result,'Rotation');
 // No non-arm runtime track, position track, binding, event, or setting may change.
 for(const p of Object.keys(rot))if(!values[p])assert.deepEqual(actual[p],rot[p],p);
 assert.deepEqual(curves(result,'Position'),pos);
 assert.equal(result.slice(result.indexOf('  m_HasGenericRootTransform:')),original.slice(original.indexOf('  m_HasGenericRootTransform:')));
 let maxWrist=0,maxWristRotation=0,maxFinger=0,maxJump=0,releaseError=0,elbowBend=0,handBackUp=0,handBackRight=0,elbowOutside=0,elbowBelow=0,reach=0;
 const fingers=Object.keys(actual).filter(p=>/\/Right(Thumb|Index|Middle|Ring|Little)/.test(p));assert.equal(fingers.length,15);
 for(let i=0;i<=1300;i++){const t=i*.0005,w=world(actual,pos,t);maxWrist=Math.max(maxWrist,angle(rotate(w[HAND].q,[0,1,0]),sub(w[HAND].p,w[ELBOW].p)));
  maxWristRotation=Math.max(maxWristRotation,qangle(sample(actual[HAND],t),neutral));
  for(const p of fingers)maxFinger=Math.max(maxFinger,qangle(sample(actual[p],t),idle[p][0].v));
  const elbowDelta=qm(sample(actual[ELBOW],t),qi(idle[ELBOW][0].v));
  assert(norm(cross(elbowDelta.slice(0,3),elbowHinge))<1e-5,'elbow must stay on the original hinge without axial twist');
  assert(dot(elbowDelta.slice(0,3),elbowHinge)>-1e-6,'no reverse elbow bend');
  if(t>=.2&&t<=.26){const back=rotate(w[HAND].q,handBack);assert(back[0]>.6&&back[1]>.18,'hand back must stay right/up during the aim hold');assert(w[ELBOW].p[0]-w[ARM].p[0]>.035&&w[ARM].p[1]-w[ELBOW].p[1]>.01,'elbow must stay outside/below shoulder during the aim hold');}
  if(i===400){releaseError=angle(rotate(qm(w[HAND].q,unit(wand)),forward),aim);elbowBend=angle(sub(w[ELBOW].p,w[ARM].p),sub(w[HAND].p,w[ELBOW].p));const back=rotate(w[HAND].q,handBack);handBackUp=back[1];handBackRight=back[0];elbowOutside=w[ELBOW].p[0]-w[ARM].p[0];elbowBelow=w[ARM].p[1]-w[ELBOW].p[1];reach=norm(sub(w[HAND].p,w[ARM].p))/(norm(sub(w[ELBOW].p,w[ARM].p))+norm(sub(w[HAND].p,w[ELBOW].p)));}
 }
 for(const p of [ARM,ELBOW,HAND]){for(let i=1;i<actual[p].length;i++)maxJump=Math.max(maxJump,qangle(actual[p][i-1].v,actual[p][i].v));assert(qangle(actual[p][0].v,idle[p][0].v)<.01);assert(qangle(actual[p].at(-1).v,idle[p][0].v)<.01);}
 console.log({maxWrist,maxFinger,releaseError,maxJump,elbowBend,handBackRight,handBackUp,reach,armChange:qangle(idle[ARM][0].v,targets[ARM]),forearmChange:qangle(idle[ELBOW][0].v,targets[ELBOW])});
 assert(maxWrist<25&&maxWristRotation<25);assert(qangle(idle[ARM][0].v,targets[ARM])<100);assert(qangle(idle[ELBOW][0].v,targets[ELBOW])<25);assert(maxFinger<.01);assert(releaseError<.01);assert(maxJump<18);assert(elbowOutside>.04);assert(elbowBelow>.02);assert(handBackRight>.6&&handBackUp>.2);assert(elbowBend>15&&elbowBend<25);assert(reach>.97&&reach<.995);
 fs.writeFileSync(filename,result);
 report.push(`${file}: original elbow hinge revision. No elbow axial twist or reverse bend across 1301 Hermite samples at 0.5 ms. Max wrist bend ${maxWrist.toFixed(5)} deg; wand presentation pitch ${pitch} deg; presentation error ${releaseError.toFixed(5)} deg; elbow flexion ${elbowBend.toFixed(2)} deg; elbow outside shoulder ${elbowOutside.toFixed(5)} m, below shoulder ${elbowBelow.toFixed(5)} m; hand-back right/up ${handBackRight.toFixed(5)}/${handBackUp.toFixed(5)}; arm reach ${(reach*100).toFixed(2)}%; max adjacent key rotation ${maxJump.toFixed(2)} deg. All 15 finger tracks equal Idle throughout (max ${maxFinger.toFixed(5)} deg). Only RightArm/RightForeArm/RightHand rotation tracks and matching editor tracks changed. Positions, other bones, event payloads/times, settings unchanged. Idle endpoints pass.`);
}
report.push('Finger and wand relative transforms retain the existing Idle grip. No socket, mount, FirePoint, mesh, weapon script, controller or Idle asset writes. Rigid relative transforms prevent animation-induced slip. Previous Unity renders/validation in NeutralWrist describe the preceding pose, not this photo revision. Visual review is left to the user as requested.');
// The report goes to the git-ignored Logs folder; the clip files above are the only asset writes.
const REPORT_DIR=path.join(ROOT,'Logs/BasicWand');
fs.mkdirSync(REPORT_DIR,{recursive:true});
fs.writeFileSync(path.join(REPORT_DIR,'neutral-wrist-validation.txt'),report.join('\n')+'\n');
console.log(report.join('\n'));
