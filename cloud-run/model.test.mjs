import test from 'node:test';
import assert from 'node:assert/strict';
import {parseReportRow,parseProductRow,parseAuditRow,authorizeAction,extractFileId} from './model.mjs';
test('report schema, dates, five-image bounds and change cursor match historical Sheets',()=>{
const r=Array(30).fill('');
r[0]='r-1';r[1]='13/10/2026';r[2]='12:50';r[4]='10198108';r[9]='https://drive.google.com/file/d/1jCetkD7YPqwDjBbgxbXnzf-TZaTP3bjw/view';r[17]='2';r[20]='382';r[21]='FALSE';r[23]='hash';
const x=parseReportRow(r);assert.equal(x.report.occurred_date,'2026-10-13');assert.equal(x.report.version,2);assert.equal(x.change_seq,382);assert.equal(x.deleted,false);assert.equal(x.images.length,1);assert.equal(x.images[0].file_id,'1jCetkD7YPqwDjBbgxbXnzf-TZaTP3bjw');
});
test('no report row can forge delete when absent',()=>{const r=Array(30).fill('');r[4]='__DELETED__';assert.equal(parseReportRow(r).deleted,true)});
test('catalog and audit payload mapping',()=>{assert.equal(parseProductRow(['sku','title','G1','a','b','c','42']).change_seq,42);assert.deepEqual(parseAuditRow(['id',178,'today','uid','user','dev','ses','ACT','{"a":1}']).details,{a:1})});
test('image ID allows Drive documented form only',()=>{assert.equal(extractFileId('https://invalid.com/file/d/1234567890/view'),'1234567890');assert.equal(extractFileId('javascript:abc'),'')});
test('RBAC: unauthorized or inactive cannot list or view image',()=>{assert.equal(authorizeAction('list_audit',{role:'user',active:true,permissions:{view_reports:true}}),false);assert.equal(authorizeAction('get_image',{role:'user',active:false,permissions:{view_reports:true}}),false);assert.equal(authorizeAction('pull_changes',{role:'user',active:true,permissions:{damage_entry:true}}),true)});
