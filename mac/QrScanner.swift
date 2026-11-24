import SwiftUI
import AppKit
import AVFoundation
import Vision
import UniformTypeIdentifiers

final class CameraQrView:NSView,AVCaptureVideoDataOutputSampleBufferDelegate {
    let session=AVCaptureSession();let queue=DispatchQueue(label:"kopra.camera");var preview:AVCaptureVideoPreviewLayer?;var found:((String)->Void)?;var failed:((String)->Void)?;var starting=false;var finished=false;var lastFrame=Date.distantPast;let frames=DispatchQueue(label:"kopra.camera.frames")
    override func viewDidMoveToWindow(){super.viewDidMoveToWindow();if window != nil && !starting{starting=true;AVCaptureDevice.requestAccess(for:.video){[weak self] allowed in DispatchQueue.main.async{guard let self,self.window != nil else{return};if allowed{self.configure()}else{self.failed?(L.text("Kein Kamerazugriff. Du kannst stattdessen ein QR-Bild öffnen."))}}}}else if window==nil{stop()}}
    func configure(){guard let camera=AVCaptureDevice.default(for:.video) else{failed?(L.text("Keine Kamera gefunden. Öffne stattdessen ein QR-Bild."));return};do{let input=try AVCaptureDeviceInput(device:camera);let output=AVCaptureVideoDataOutput();output.alwaysDiscardsLateVideoFrames=true;session.beginConfiguration();guard session.canAddInput(input),session.canAddOutput(output) else{session.commitConfiguration();throw SharedError.unavailable};session.addInput(input);session.addOutput(output);output.setSampleBufferDelegate(self,queue:frames);session.commitConfiguration();wantsLayer=true;let preview=AVCaptureVideoPreviewLayer(session:session);preview.videoGravity = .resizeAspectFill;preview.frame=bounds;layer?.addSublayer(preview);self.preview=preview;queue.async{[weak self] in self?.session.startRunning()}}catch{failed?(L.text("Die Kamera konnte nicht gestartet werden."))}}
    override func layout(){super.layout();preview?.frame=bounds}
    func captureOutput(_ output:AVCaptureOutput,didOutput sampleBuffer:CMSampleBuffer,from connection:AVCaptureConnection){
        guard !finished,Date().timeIntervalSince(lastFrame)>0.25 else{return};lastFrame=Date();let task=VNDetectBarcodesRequest();task.symbologies=[.qr]
        do{try VNImageRequestHandler(cmSampleBuffer:sampleBuffer,orientation:.up,options:[:]).perform([task]);if let code=task.results?.compactMap({$0.payloadStringValue}).first(where:{$0.hasPrefix("kopra://pair?")}){finished=true;DispatchQueue.main.async{[weak self] in guard let self,self.window != nil else{return};self.stop();self.found?(code)}}}catch{}
    }
    func stop(){let active=session;queue.async{active.stopRunning()}}
}
struct CameraScanner:NSViewRepresentable {
    var found:(String)->Void;var failed:(String)->Void
    func makeNSView(context:Context)->CameraQrView{let view=CameraQrView();view.found=found;view.failed=failed;return view}
    func updateNSView(_ nsView:CameraQrView,context:Context){}
    static func dismantleNSView(_ nsView:CameraQrView,coordinator:()){nsView.stop()}
}
@MainActor func qrFromImage() throws -> String? {
    let picker=NSOpenPanel();picker.title="Kopra-QR-Code öffnen";picker.allowedContentTypes=[.image];guard picker.runModal() == .OK,let url=picker.url else{return nil};let task=VNDetectBarcodesRequest();task.symbologies=[.qr];try VNImageRequestHandler(url:url,options:[:]).perform([task]);let values=task.results?.compactMap{$0.payloadStringValue}.filter{$0.hasPrefix("kopra://pair?")} ?? [];guard values.count==1 else{throw SharedError.invalid};return values[0]
}
