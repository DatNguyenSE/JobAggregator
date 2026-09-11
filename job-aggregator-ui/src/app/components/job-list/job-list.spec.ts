import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { JobListComponent } from './job-list';
import { JobService, JobPost, JobSearchResponse } from '../../services/job';

describe('DB first search', () => {
  let fixture: ComponentFixture<JobListComponent>;
  let component: JobListComponent;
  let api: jasmine.SpyObj<JobService>;
  const job = (id: string): JobPost => ({ id, externalId: id, platform: 'ViecLam24h', title: 'Sale', sourceUrl: '#', postedDate: '2026-09-10' });
  const response = (jobs: JobPost[], pending: boolean): JobSearchResponse => ({ requestId: 'request-1', jobs,
    requestedCount: 3, missingCount: 3 - jobs.length, status: pending ? 'pending' : 'complete', isScraping: pending });
  beforeEach(() => {
    api = jasmine.createSpyObj<JobService>('JobService', ['searchJobs', 'getSearchStatus']);
    TestBed.configureTestingModule({ imports: [JobListComponent], providers: [{ provide: JobService, useValue: api }] });
    fixture = TestBed.createComponent(JobListComponent); component = fixture.componentInstance;
    spyOn(component, 'connectWebSocket'); fixture.detectChanges();
    component.criteria.Keyword = 'sale'; component.criteria.MaxJobs = 3;
  });
  afterEach(() => fixture.destroy());
  it('renders cached records while top-up is pending and polls without another POST', fakeAsync(() => {
    api.searchJobs.and.returnValue(of(response([job('1')], true)));
    api.getSearchStatus.and.returnValue(of(response([job('1'), job('2'), job('3')], false)));
    component.searchJobs(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.job-card').length).toBe(1);
    expect(component.isLoading).toBeTrue();
    tick(3000); fixture.detectChanges();
    expect(component.jobs.length).toBe(3); expect(component.isLoading).toBeFalse();
    expect(api.searchJobs).toHaveBeenCalledTimes(1);
  }));
  it('does not poll when the DB already contains enough fresh jobs', fakeAsync(() => {
    api.searchJobs.and.returnValue(of(response([job('1'), job('2'), job('3')], false)));
    component.searchJobs(); tick(5000);
    expect(api.getSearchStatus).not.toHaveBeenCalled();
  }));
  it('deduplicates and caps results, then shows a partial completion message', () => {
    api.searchJobs.and.returnValue(of(response([job('1'), job('1')], false)));
    component.searchJobs(); fixture.detectChanges();
    expect(component.jobs.length).toBe(1); expect(component.message).toContain('1/3');
    expect(fixture.nativeElement.querySelector('.error-alert').textContent).toContain('1/3');
  });
  it('ignores a late response belonging to the previous search', () => {
    const old = new Subject<JobSearchResponse>();
    api.searchJobs.and.returnValues(old, of(response([job('new')], false)));
    component.searchJobs(); component.selectedProvince = 'An Giang'; component.searchJobs();
    old.next(response([job('old')], false));
    expect(component.jobs[0].id).toBe('new');
  });
  it('stops waiting on empty source completion', fakeAsync(() => {
    api.searchJobs.and.returnValue(of(response([], true)));
    api.getSearchStatus.and.returnValue(of(response([], false)));
    component.searchJobs(); tick(3000);
    expect(component.isLoading).toBeFalse(); expect(component.message).toContain('Không tìm thấy');
  }));
});
